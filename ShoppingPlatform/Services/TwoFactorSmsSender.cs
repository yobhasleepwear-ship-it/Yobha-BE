using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public class TwoFactorSmsSender : ISmsSender
    {
        private readonly TwoFactorService _svc;
        private readonly TwoFactorSettings _cfg;
        private readonly string _apiKey;
        private readonly ILogger<TwoFactorSmsSender> _logger;
        private readonly IHttpClientFactory _httpFactory;

        // Defaults — we still prefer configured values when available
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

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("TwoFactor API key not configured (checked TwoFactor:ApiKey, env vars, settings).");
            }
            else
            {
                _logger.LogInformation("TwoFactor API key loaded (masked={keyMasked})", MaskKey(_apiKey));
            }
        }

        // Generate OTP and call 2Factor provider
        public async Task<ProviderResult> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException(nameof(phoneNumber));

            // create an OTP
            var otp = new Random().Next(100000, 999999).ToString();

            var var1 = _cfg?.DefaultVar1 ?? "Customer";
            var var2 = otp;

            _logger.LogDebug("TwoFactor: prepare send phone={phoneMask}", MaskPhone(phoneNumber));

            var normalized = NormalizePhone(phoneNumber);

            // Prefer configured template/sender but fallback to defaults
            var template = !string.IsNullOrWhiteSpace(_cfg?.TemplateId) ? _cfg.TemplateId : DefaultTemplateName;
            var sender = !string.IsNullOrWhiteSpace(_cfg?.SenderId) ? _cfg.SenderId : DefaultSenderId;

            // IMPORTANT: Use the template id in the final path segment (not the sender).
            // This matches 2factor.in examples and ensures SMS routing (avoids voice fallback).
            var url = $"https://2factor.in/API/V1/{_apiKey}/SMS/{normalized}/AUTOGEN/{template}";

            // Form body: template and variables only. Do NOT send From/To in form when using this path.
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("TemplateName", template),
                new KeyValuePair<string, string>("VAR1", var1),
                new KeyValuePair<string, string>("VAR2", var2)
            };

            _logger.LogInformation("TwoFactor POST -> urlMask={urlMask} phone={phoneMask} template={template} sender={sender}",
                MaskKey(url), MaskPhone(phoneNumber), template, sender);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            string body;
            try
            {
                var client = _httpFactory.CreateClient();
                resp = await client.SendAsync(request);
                body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor HTTP call failed for phone={phoneMask}", MaskPhone(phoneNumber));
                return new ProviderResult { IsSuccess = false, ProviderStatus = "HTTP_ERROR", RawResponse = ex.Message };
            }

            _logger.LogInformation("TwoFactor HTTP response status={status} phone={phoneMask} bodyLen={len}", (int)resp.StatusCode, MaskPhone(phoneNumber), body?.Length ?? 0);
            _logger.LogDebug("TwoFactor body: {body}", Truncate(body, 2000));

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
                    _logger.LogInformation("TwoFactor accepted; session={sid} phone={phoneMask}", result.SessionId, MaskPhone(phoneNumber));
                else
                    _logger.LogWarning("TwoFactor not-accepted; status={status} details={details}", status, Truncate(details, 500));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor: parse error");
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

        private static string MaskPhone(string? phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var digits = Regex.Replace(phone, @"\D", "");
            if (digits.Length <= 4) return new string('*', digits.Length);
            return new string('*', digits.Length - 4) + digits[^4..];
        }

        private static string MaskKey(string s, int showRight = 4)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= showRight) return new string('*', s.Length);
            return new string('*', s.Length - showRight) + s[^showRight..];
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
