// File: Services/TwoFactorSmsSender.cs
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Sms;
using static System.Net.WebRequestMethods;

namespace ShoppingPlatform.Services
{
    public class TwoFactorSmsSender : ISmsSender
    {
        private readonly TwoFactorService _svc;
        private readonly TwoFactorSettings _cfg;
        private readonly string _apiKey;
        private readonly ILogger<TwoFactorSmsSender> _logger;
        private readonly IHttpClientFactory _httpFactory;


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

            var fromConfig = configuration["TwoFactor:ApiKey"];
            var fromSettings = _cfg?.ApiKey;
            var fromEnvDouble = Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY");
            var fromEnvSingle = Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY");

            _apiKey = !string.IsNullOrWhiteSpace(fromConfig) ? fromConfig
                    : !string.IsNullOrWhiteSpace(fromSettings) ? fromSettings
                    : !string.IsNullOrWhiteSpace(fromEnvDouble) ? fromEnvDouble
                    : fromEnvSingle ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_apiKey))
                _logger.LogError("TwoFactor API key not configured (checked TwoFactor:ApiKey, env vars, settings).");
            else
                _logger.LogInformation("TwoFactor API key loaded (len={len})", _apiKey.Length);
            _httpFactory = httpFactory;
        }

        // Generate OTP and call TwoFactorService
        public async Task<ProviderResult> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException(nameof(phoneNumber));

            // generate OTP locally (can be removed if provider generates OTP)
            var otp = new Random().Next(100000, 999999).ToString();

            var var1 = _cfg.DefaultVar1 ?? "Customer";
            var var2 = otp;

            _logger.LogInformation("Preparing to send OTP; phone={phoneMask} template={template} sender={sender}",
                MaskPhone(phoneNumber), _cfg.TemplateName ?? _cfg.TemplateId ?? "unknown", _cfg.SenderId ?? "YOBHAS");

            static string NormalizePhone(string phone)
            {
                var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
                if (digits.Length == 10) return "91" + digits;
                if (digits.Length == 11 && digits.StartsWith("0")) return "91" + digits.Substring(1);
                if (digits.StartsWith("91") && digits.Length >= 12) return digits;
                return digits;
            }

            var normalizedPhone = NormalizePhone(phoneNumber);
            var templateName = _cfg.TemplateName ?? _cfg.TemplateId ?? "SENDOTP";
            var sender = _cfg.SenderId ?? "YOBHAS";

            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("From", sender),
                new KeyValuePair<string, string>("To", normalizedPhone),
                new KeyValuePair<string, string>("TemplateName", templateName),
            };

            if (!string.IsNullOrWhiteSpace(var1)) form.Add(new KeyValuePair<string, string>("VAR1", var1));
            if (!string.IsNullOrWhiteSpace(var2)) form.Add(new KeyValuePair<string, string>("VAR2", var2));

            // Use normalizedPhone in URL
            var url = $"https://2factor.in/API/V1/{_apiKey}/SMS/{normalizedPhone}/AUTOGEN/{sender}";

            _logger.LogDebug("TwoFactor: sending POST to {urlMasked} with keys={keys}",
                MaskKey(url, 16), string.Join(',', form.Select(f => f.Key)));

            var http = _httpFactory.CreateClient("TwoFactorClient");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            string body;
            try
            {
                resp = await http.SendAsync(request);
                body = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor: HTTP request failed for phone={phoneMask}", MaskPhone(phoneNumber));
                return new ProviderResult { IsSuccess = false, ProviderStatus = "HTTP_ERROR", RawResponse = ex.Message };
            }

            _logger.LogInformation("TwoFactor response: httpStatus={status} phone={phoneMask} bodyLen={len}",
                (int)resp.StatusCode, MaskPhone(phoneNumber), body?.Length ?? 0);
            _logger.LogDebug("TwoFactor response body (truncated): {body}", Truncate(body, 1000));

            var result = new ProviderResult { RawResponse = body ?? string.Empty };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                // parse with System.Text.Json
                using var doc = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var root = doc.RootElement;

                string status = "";
                if (root.TryGetProperty("Status", out var st)) status = st.GetString() ?? "";
                else if (root.TryGetProperty("status", out var st2)) status = st2.GetString() ?? "";

                string details = "";
                if (root.TryGetProperty("Details", out var d)) details = d.GetString() ?? "";
                else if (root.TryGetProperty("details", out var d2)) details = d2.GetString() ?? "";

                result.ProviderStatus = status;
                result.SessionId = details;
                result.ProviderMessageId = root.TryGetProperty("MessageId", out var mid) ? mid.GetString() : null;

                result.IsSuccess = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

                if (result.IsSuccess)
                {
                    _logger.LogInformation("TwoFactor accepted OTP send. sessionId={sid} phone={phoneMask}", result.SessionId, MaskPhone(phoneNumber));
                }
                else
                {
                    _logger.LogWarning("TwoFactor returned non-success status={status} details={details}", status, Truncate(details, 200));
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor: failed to parse provider response body");
                result.IsSuccess = false;
                result.ProviderStatus = "PARSE_ERROR";
                return result;
            }

            static string MaskPhone(string? phone)
            {
                if (string.IsNullOrEmpty(phone)) return string.Empty;
                var digits = Regex.Replace(phone, @"\D", "");
                if (digits.Length <= 4) return new string('*', digits.Length);
                return new string('*', digits.Length - 4) + digits[^4..];
            }
            static string Truncate(string? s, int length) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= length ? s : s.Substring(0, length) + "...");
            static string MaskKey(string s, int showRight = 4)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                if (s.Length <= showRight) return new string('*', s.Length);
                return new string('*', s.Length - showRight) + s[^showRight..];
            }
        }

        public async Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            return await _svc.VerifyOtpAsync(_apiKey, sessionId, otp);
        }
    }
}
