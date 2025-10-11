// File: Sms/TwoFactorService.cs
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShoppingPlatform.DTOs;
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShoppingPlatform.Sms
{
    public class TwoFactorService
    {
        private readonly HttpClient _http;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(HttpClient http, ILogger<TwoFactorService> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // POST template SMS and return ProviderResult
        public async Task<ProviderResult> SendOtpAsync(
    string apiKey,
    string phoneNumber,
    string? senderId = "YOBHAS",
    string templateName = "OTPSendTemplate1",
    string? var1 = null,
    string? var2 = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException(nameof(apiKey));
            if (string.IsNullOrWhiteSpace(phoneNumber)) throw new ArgumentException(nameof(phoneNumber));

            static string NormalizePhone(string phone)
            {
                var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
                if (digits.Length == 10) return "91" + digits;
                if (digits.Length == 11 && digits.StartsWith("0")) return "91" + digits.Substring(1);
                if (digits.StartsWith("91") && digits.Length >= 12) return digits;
                return digits;
            }
            var normalizedPhone = NormalizePhone(phoneNumber);

            // Build form fields (omit empty vars)
            var form = new List<KeyValuePair<string, string>>
    {
        new KeyValuePair<string, string>("From", senderId ?? "YOBHAS"),
        new KeyValuePair<string, string>("To", normalizedPhone),
        new KeyValuePair<string, string>("TemplateName", templateName)
    };
            if (!string.IsNullOrWhiteSpace(var1)) form.Add(new KeyValuePair<string, string>("VAR1", var1!));
            if (!string.IsNullOrWhiteSpace(var2)) form.Add(new KeyValuePair<string, string>("VAR2", var2!));

            var url = $"https://2factor.in/API/V1/{apiKey}/ADDON_SERVICES/SEND/TSMS";

            // Mask API key in logs
            string MaskKey(string s, int showRight = 4)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                if (s.Length <= showRight) return new string('*', s.Length);
                return new string('*', s.Length - showRight) + s[^showRight..];
            }

            _logger.LogInformation("TwoFactor.SendOtp -> sending to {to} template={template} apiKey={keyMasked}",
                normalizedPhone, templateName, MaskKey(apiKey));

            var requestContent = new FormUrlEncodedContent(form);

            // Provide accept header for explicitness
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = requestContent
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await _http.SendAsync(request);
            var body = await resp.Content.ReadAsStringAsync();

            _logger.LogInformation("TwoFactor response HTTP {code} bodyLength={len}", (int)resp.StatusCode, body?.Length ?? 0);
            _logger.LogDebug("TwoFactor response body: {body}", body);

            var result = new ProviderResult { RawResponse = body ?? string.Empty };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                // typical 2Factor shape: { "Status":"Success","Details":"<sessionId>" }
                var parsed = JsonConvert.DeserializeObject<JObject?>(body ?? "{}");
                var status = parsed?["Status"]?.ToString() ?? parsed?["status"]?.ToString() ?? string.Empty;
                var details = parsed?["Details"]?.ToString() ?? parsed?["details"]?.ToString() ?? string.Empty;

                result.ProviderStatus = status;
                result.SessionId = details;

                // message id may be in a different field
                result.ProviderMessageId = parsed?["MessageId"]?.ToString()
                                           ?? parsed?["message_id"]?.ToString()
                                           ?? parsed?["messageId"]?.ToString()
                                           ?? string.Empty;

                // treat explicit "Success" or "Accepted" as success; do NOT treat presence of details alone as success
                result.IsSuccess = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

                // fallback: some providers return blank status but give a details sessionId
                if (!result.IsSuccess && !string.IsNullOrWhiteSpace(details))
                {
                    // keep it false but record raw response for manual inspection
                    _logger.LogWarning("TwoFactor: unexpected success fallback - status='{status}' details='{details}'", status, details);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor: failed to parse provider response");
                result.IsSuccess = false;
                result.ProviderStatus = "PARSE_ERROR";
                return result;
            }
        }


        // Verify OTP
        public async Task<bool> VerifyOtpAsync(string apiKey, string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId");
            if (string.IsNullOrWhiteSpace(otp)) throw new ArgumentException("otp");

            var url = $"https://2factor.in/API/V1/{apiKey}/SMS/VERIFY/{sessionId}/{otp}";
            _logger.LogInformation("TwoFactor.VerifyOtp -> session={session}", sessionId);

            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogDebug("TwoFactor.VerifyOtp response: {body}", body);

            if (!resp.IsSuccessStatusCode) return false;

            try
            {
                dynamic parsed = JsonConvert.DeserializeObject(body);
                string status = parsed?.Status ?? string.Empty;
                return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
