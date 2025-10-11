using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ShoppingPlatform.Sms
{
    public class TwoFactorService
    {
        private readonly HttpClient _http;
        public TwoFactorService(HttpClient http)
        {
            _http = http;
        }

        // Sends a template SMS and returns session id (or throws on failure)
        public async Task<string> SendOtpAsync(string apiKey, string phoneNumber, string? senderId = null, string templateName = "OTPSendTemplate1", string? var1 = null, string? var2 = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(phoneNumber)) throw new ArgumentException("phoneNumber");

            var url = $"https://2factor.in/API/V1/{apiKey}/ADDON_SERVICES/SEND/TSMS";

            var payload = new
            {
                From = senderId ?? "YOBHAS",        // use configured SenderId or fallback
                To = phoneNumber,
                TemplateName = templateName,
                VAR1 = var1 ?? string.Empty,
                VAR2 = var2 ?? string.Empty
            };

            var json = JsonConvert.SerializeObject(payload);
            var resp = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"2Factor Send failed ({(int)resp.StatusCode}): {body}");

            // 2Factor returns JSON like: { "Status":"Success","Details":"<sessionId>" }
            dynamic parsed = JsonConvert.DeserializeObject(body);
            string details = parsed?.Details ?? throw new InvalidOperationException("2Factor response missing Details");
            return details;
        }

        // Verifies an OTP given the sessionId and the OTP value
        public async Task<bool> VerifyOtpAsync(string apiKey, string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId");
            if (string.IsNullOrWhiteSpace(otp)) throw new ArgumentException("otp");

            // 2Factor verify endpoint for session-based verification
            var url = $"https://2factor.in/API/V1/{apiKey}/SMS/VERIFY/{sessionId}/{otp}";
            var resp = await _http.GetAsync(url);

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return false;

            // Response example: { "Status":"Success", "Details":"OTP Matched" }
            dynamic parsed = JsonConvert.DeserializeObject(body);
            string status = parsed?.Status ?? string.Empty;
            return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
        }
    }
}
