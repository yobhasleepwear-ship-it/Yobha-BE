// File: Sms/TwoFactorService.cs
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(phoneNumber)) throw new ArgumentException("phoneNumber");

            // normalize phone (E.164-ish)
            string NormalizePhone(string phone)
            {
                var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
                if (digits.Length == 10) return "91" + digits;
                if (digits.StartsWith("0") && digits.Length == 11) return "91" + digits.Substring(1);
                return digits;
            }
            var normalizedPhone = NormalizePhone(phoneNumber);

            var url = $"https://2factor.in/API/V1/{apiKey}/ADDON_SERVICES/SEND/TSMS";

            var payload = new
            {
                From = senderId ?? "YOBHAS",
                To = normalizedPhone,
                TemplateName = templateName,
                VAR1 = var1 ?? string.Empty,
                VAR2 = var2 ?? string.Empty
            };

            var json = JsonConvert.SerializeObject(payload);

            // masked log to confirm apiKey presence
            string Mask(string s, int showRight = 4)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                if (s.Length <= showRight) return new string('*', s.Length);
                return new string('*', s.Length - showRight) + s[^showRight..];
            }

            _logger.LogInformation("TwoFactor.SendOtp -> sending to {to} template={template} apiKey={keyMasked}", normalizedPhone, templateName, Mask(apiKey));
            _logger.LogDebug("TwoFactor.SendOtp payload: {payload}", json);

            var resp = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();

            _logger.LogInformation("TwoFactor response HTTP {code} bodyLength={len}", (int)resp.StatusCode, body?.Length ?? 0);
            _logger.LogDebug("TwoFactor response body: {body}", body);

            var result = new ProviderResult { RawResponse = body };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                dynamic parsed = JsonConvert.DeserializeObject(body);
                string status = parsed?.Status ?? string.Empty;
                string details = parsed?.Details ?? string.Empty;

                result.ProviderStatus = status ?? string.Empty;
                result.SessionId = details ?? string.Empty;
                // some providers include message id in another field; parse if present
                result.ProviderMessageId = parsed?.MessageId ?? parsed?.message_id ?? string.Empty;

                result.IsSuccess = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase)
                                   || !string.IsNullOrWhiteSpace(details); // fallback

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
