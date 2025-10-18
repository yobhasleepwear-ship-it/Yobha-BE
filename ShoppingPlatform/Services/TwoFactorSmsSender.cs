using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Sms;

namespace ShoppingPlatform.Services
{
    /// <summary>
    /// TwoFactor SMS sender with two modes:
    ///  - Template mode (default): uses AUTOGEN/{template} and VAR2 for OTP
    ///  - PlainText mode (override): posts JSON to send-sms endpoint with explicit message text
    /// 
    /// Configure 'TwoFactor:UseTemplate' in appsettings.json (true|false).
    /// </summary>
    public class TwoFactorSmsSender : ISmsSender
    {
        private readonly TwoFactorService _svc;
        private readonly TwoFactorSettings _cfg;
        private readonly string _apiKey;
        private readonly ILogger<TwoFactorSmsSender> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly bool _useTemplate;

        private const string DefaultTemplateName = "SENDOTP";
        private const string DefaultSenderId = "YOBHAS";

        public TwoFactorSmsSender(
            TwoFactorService svc,
            IOptions<TwoFactorSettings> opts,
            IConfiguration configuration,
            ILogger<TwoFactorSmsSender> logger,
            IHttpClientFactory httpFactory)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _cfg = opts?.Value ?? new TwoFactorSettings();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));

            // Resolve API key (same lookup strategy)
            var fromConfig = configuration["TwoFactor:ApiKey"];
            var fromSettings = _cfg?.ApiKey;
            var fromEnvDouble = Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY");
            var fromEnvSingle = Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY");

            _apiKey = !string.IsNullOrWhiteSpace(fromConfig) ? fromConfig
                    : !string.IsNullOrWhiteSpace(fromSettings) ? fromSettings
                    : !string.IsNullOrWhiteSpace(fromEnvDouble) ? fromEnvDouble
                    : fromEnvSingle ?? string.Empty;

            // Mode: template vs plain text (default true)
            var useTemplateCfg = configuration["TwoFactor:UseTemplate"];
            if (!string.IsNullOrWhiteSpace(useTemplateCfg) && bool.TryParse(useTemplateCfg, out var parsed))
                _useTemplate = parsed;
            else
                _useTemplate = _cfg.UseTemplate ?? true;

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("TwoFactor API key not configured (checked TwoFactor:ApiKey, env vars, settings).");
            }
            else
            {
                _logger.LogInformation("TwoFactor API key loaded (masked={keyMasked})", MaskKey(_apiKey));
            }

            _logger.LogInformation("TwoFactor mode: UseTemplate={useTemplate}", _useTemplate);
        }

        /// <summary>
        /// Send OTP - either via configured template (recommended) OR via plain text message (override).
        /// Returns ProviderResult with RawResponse containing provider JSON (for debugging).
        /// </summary>
        public async Task<ProviderResult> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException(nameof(phoneNumber));

            var otp = new Random().Next(100000, 999999).ToString();
            var var1 = _cfg?.DefaultVar1 ?? "Customer";
            var var2 = otp;

            _logger.LogDebug("TwoFactor: prepare send phone={phoneMask}", MaskPhone(phoneNumber));

            var normalized = NormalizePhone(phoneNumber);
            var template = !string.IsNullOrWhiteSpace(_cfg?.TemplateId) ? _cfg.TemplateId : DefaultTemplateName;
            var sender = !string.IsNullOrWhiteSpace(_cfg?.SenderId) ? _cfg.SenderId : DefaultSenderId;

            // If configured to use template, call AUTOGEN/{template}
            if (_useTemplate)
            {
                return await SendUsingTemplateAsync(normalized, template, sender, var1, var2, phoneNumber);
            }

            // Otherwise send explicit message text (plain SMS)
            return await SendUsingPlainTextAsync(normalized, sender, var2, phoneNumber);
        }

        private async Task<ProviderResult> SendUsingTemplateAsync(string normalized, string template, string sender, string var1, string var2, string originalPhone)
        {
            // Use template in final path
            var url = $"https://2factor.in/API/V1/{_apiKey}/SMS/{normalized}/AUTOGEN/{template}";

            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("TemplateName", template),
                new KeyValuePair<string, string>("VAR1", var1),
                new KeyValuePair<string, string>("VAR2", var2)
            };

            // Masked info for logs
            var apiKeyMasked = MaskKey(_apiKey, showRight: 4);
            var formBodyString = string.Join("&", form.ConvertAll(kv => $"{WebUtility.UrlEncode(kv.Key)}={WebUtility.UrlEncode(kv.Value)}"));

            _logger.LogInformation("TwoFactor (template) Request -> template={template} sender={sender} phone={phoneMask} url={url} apiKeyMasked={apiKeyMasked} formBody={formBody}",
                template, sender, MaskPhone(originalPhone), MaskKey(url), apiKeyMasked, formBodyString);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            string body;
            try
            {
                var client = _httpFactory.CreateClient("TwoFactorClient");
                resp = await client.SendAsync(request);
                body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor HTTP (template) call failed for phone={phoneMask}", MaskPhone(originalPhone));
                return new ProviderResult { IsSuccess = false, ProviderStatus = "HTTP_ERROR", RawResponse = ex.Message };
            }

            _logger.LogInformation("TwoFactor (template) response httpStatus={status} phone={phoneMask} bodyLen={len}", (int)resp.StatusCode, MaskPhone(originalPhone), body?.Length ?? 0);
            _logger.LogDebug("TwoFactor (template) body: {body}", Truncate(body, 2000));

            var result = new ProviderResult { RawResponse = body ?? "" };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<JObject?>(body ?? "{}");
                var status = parsed?["Status"]?.ToString() ?? parsed?["status"]?.ToString() ?? "";
                var details = parsed?["Details"]?.ToString() ?? parsed?["details"]?.ToString() ?? "";

                result.ProviderStatus = status;
                result.SessionId = details;
                result.ProviderMessageId = parsed?["MessageId"]?.ToString() ?? "";

                result.IsSuccess = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

                if (result.IsSuccess)
                    _logger.LogInformation("TwoFactor (template) accepted; session={sid} phone={phoneMask}", result.SessionId, MaskPhone(originalPhone));
                else
                    _logger.LogWarning("TwoFactor (template) not-accepted; status={status} details={details}", status, Truncate(details, 500));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor (template): parse error");
                result.IsSuccess = false;
                result.ProviderStatus = "PARSE_ERROR";
                return result;
            }
        }

        private async Task<ProviderResult> SendUsingPlainTextAsync(string normalized, string sender, string otp, string originalPhone)
        {
            // Build the exact message text we want the user to receive
            // WARNING: Make sure this text complies with DLT rules in your country.
            var messageText = $"{otp} is your OTP to verify phone number at Yobha Sleepwear. Please do not share OTP with anyone.";

            // 2Factor has a JSON send-sms endpoint (documented on their site).
            // Use the v1 JSON endpoint so that we can pass explicit message text.
            var url = "https://api.2factor.in/SMS/v1/send-sms"; // documented JSON endpoint

            // Build JSON body
            var payload = new
            {
                api_key = _apiKey,
                to = normalized.StartsWith("91") ? "+" + normalized : "+" + normalized,
                message = messageText,
                sender_id = sender
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Mask info for logs
            var apiKeyMasked = MaskKey(_apiKey, showRight: 4);
            _logger.LogInformation("TwoFactor (plain) Request -> sender={sender} to={phoneMask} url={url} apiKeyMasked={apiKeyMasked} messagePreview={preview}",
                sender, MaskPhone(originalPhone), MaskKey(url), apiKeyMasked, Truncate(messageText, 80));

            HttpResponseMessage resp;
            string body;
            try
            {
                var client = _httpFactory.CreateClient("TwoFactorClient");
                resp = await client.PostAsync(url, content);
                body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor HTTP (plain) call failed for phone={phoneMask}", MaskPhone(originalPhone));
                return new ProviderResult { IsSuccess = false, ProviderStatus = "HTTP_ERROR", RawResponse = ex.Message };
            }

            _logger.LogInformation("TwoFactor (plain) response httpStatus={status} phone={phoneMask} bodyLen={len}", (int)resp.StatusCode, MaskPhone(originalPhone), body?.Length ?? 0);
            _logger.LogDebug("TwoFactor (plain) body: {body}", Truncate(body, 2000));

            var result = new ProviderResult { RawResponse = body ?? "" };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<JObject?>(body ?? "{}");
                // 2factor's JSON structure may differ; adapt if needed
                var status = parsed?["status"]?.ToString() ?? parsed?["Status"]?.ToString() ?? "";
                var details = parsed?["message"]?.ToString() ?? parsed?["Details"]?.ToString() ?? parsed?["details"]?.ToString() ?? "";

                result.ProviderStatus = status;
                result.SessionId = parsed?["sid"]?.ToString() ?? parsed?["Details"]?.ToString() ?? "";

                result.IsSuccess = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase);

                if (result.IsSuccess)
                    _logger.LogInformation("TwoFactor (plain) accepted; session={sid} phone={phoneMask}", result.SessionId, MaskPhone(originalPhone));
                else
                    _logger.LogWarning("TwoFactor (plain) not-accepted; status={status} details={details}", status, Truncate(details, 500));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor (plain): parse error");
                result.IsSuccess = false;
                result.ProviderStatus = "PARSE_ERROR";
                return result;
            }
        }

        public async Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            // delegate verify call to TwoFactorService (existing behavior)
            return await _svc.VerifyOtpAsync(_apiKey, sessionId, otp);
        }

        // ---------------- helpers ----------------
        private static string Truncate(string? s, int length) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= length ? s : s.Substring(0, length) + "...");

        private static string MaskKey(string s, int showRight = 4)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= showRight) return new string('*', s.Length);
            return new string('*', s.Length - showRight) + s[^showRight..];
        }

        private static string MaskPhone(string? phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var digits = Regex.Replace(phone, @"\D", "");
            if (digits.Length <= 4) return new string('*', digits.Length);
            return new string('*', digits.Length - 4) + digits[^4..];
        }

        private static string NormalizePhone(string phone)
        {
            var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
            if (digits.Length == 10) return "91" + digits;
            if (digits.Length == 11 && digits.StartsWith("0")) return "91" + digits.Substring(1);
            if (digits.StartsWith("91") && digits.Length >= 12) return digits;
            return digits;
        }
    }
}
