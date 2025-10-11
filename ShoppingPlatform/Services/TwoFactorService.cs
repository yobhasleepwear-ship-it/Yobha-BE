using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        // --- Sends OTP using approved 2Factor DLT template ---
        public async Task<string> SendOtpAsync(
            string apiKey,
            string phoneNumber,
            string? senderId = "YOBHAS",
            string templateName = "OTPSendTemplate1",
            string? var1 = null,
            string? var2 = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key is missing.", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException("Phone number is required.", nameof(phoneNumber));

            // Normalize phone (ensure 91 prefix)
            string NormalizePhone(string phone)
            {
                var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
                if (digits.Length == 10) return "91" + digits;
                if (digits.StartsWith("0") && digits.Length == 11) return "91" + digits.Substring(1);
                return digits;
            }

            var normalizedPhone = NormalizePhone(phoneNumber);

            // Mask key for logs
            string Mask(string s, int showRight = 4)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                if (s.Length <= showRight) return new string('*', s.Length);
                return new string('*', s.Length - showRight) + s[^showRight..];
            }

            // Build request
            var url = $"https://2factor.in/API/V1/{apiKey}/ADDON_SERVICES/SEND/TSMS";
            var payload = new
            {
                From = senderId ?? "YOBHAS",
                To = normalizedPhone,
                TemplateName = templateName,
                VAR1 = var1 ?? "",
                VAR2 = var2 ?? ""
            };

            var json = JsonConvert.SerializeObject(payload);

            // Log outgoing request (safe)
            _logger.LogInformation("2Factor SMS Request -> To={phone}, Sender={sender}, Template={template}, ApiKey={keyMasked}",
                normalizedPhone, senderId, templateName, Mask(apiKey));
            _logger.LogDebug("2Factor SMS Payload: {payload}", json);

            var response = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("2Factor SMS Response: HTTP {statusCode}, Body={body}",
                (int)response.StatusCode, body);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"2Factor send failed ({(int)response.StatusCode}): {body}");

            dynamic parsed = JsonConvert.DeserializeObject(body);
            string status = parsed?.Status ?? "Unknown";
            string details = parsed?.Details ?? string.Empty;

            if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("2Factor SMS Rejected. Status={status}, Details={details}", status, details);
                throw new InvalidOperationException($"2Factor rejected message. Status={status}, Details={details}");
            }

            _logger.LogInformation("2Factor OTP sent successfully. SessionId={sessionId}", details);
            return details;
        }

        // --- Verifies OTP ---
        public async Task<bool> VerifyOtpAsync(string apiKey, string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("sessionId");
            if (string.IsNullOrWhiteSpace(otp))
                throw new ArgumentException("otp");

            var url = $"https://2factor.in/API/V1/{apiKey}/SMS/VERIFY/{sessionId}/{otp}";

            _logger.LogInformation("Verifying OTP with 2Factor -> Session={session}, OTP={otpMasked}", sessionId, "***");
            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("2Factor Verify Response: {body}", body);

            if (!resp.IsSuccessStatusCode)
                return false;

            dynamic parsed = JsonConvert.DeserializeObject(body);
            string status = parsed?.Status ?? string.Empty;

            return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
        }
    }
}
