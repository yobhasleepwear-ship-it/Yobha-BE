using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShoppingPlatform.Sms
{
    public class TwoFactorService
    {
        private readonly HttpClient _http;
        private readonly TwoFactorSettings _cfg;
        public TwoFactorService(HttpClient http, IOptions<TwoFactorSettings> opts)
        {
            _http = http;
            _cfg = opts.Value ?? new TwoFactorSettings();
        }

        // Sends OTP. Returns session id (string) on success, throws on failure.
        public async Task<string> SendOtpAsync(string apiKey, string phoneNumber, string? senderId = null)
        {
            // 2factor endpoint format:
            // https://2factor.in/API/V1/{API_KEY}/SMS/{MOBILE_NUMBER}/AUTOGEN/{OTP_TEMPLATE}
            // We'll use AUTOGEN (auto-generate OTP). No template passed.
            var baseUrl = _cfg.BaseUrl?.TrimEnd('/') ?? "https://2factor.in";
            var url = $"{baseUrl}/API/V1/{apiKey}/SMS/{phoneNumber}/AUTOGEN";

            var resp = await _http.GetAsync(url);
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"2Factor send OTP failed: {resp.StatusCode} {content}");
            }

            // Example success JSON:
            // {"Status":"Success","Details":"<SESSION_ID>"}
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("Status", out var s) && s.GetString() == "Success"
                    && root.TryGetProperty("Details", out var d))
                {
                    return d.GetString() ?? throw new Exception("2Factor returned empty session id");
                }

                // On failure: {"Status":"Error", "Details":"Invalid API Key"}
                var details = root.TryGetProperty("Details", out var dd) ? dd.GetString() : content;
                throw new Exception($"2Factor response error: {details}");
            }
            catch (JsonException)
            {
                throw new Exception($"Unrecognized 2Factor response: {content}");
            }
        }

        // Verify OTP using session id + otp
        public async Task<bool> VerifyOtpAsync(string apiKey, string sessionId, string otp)
        {
            var baseUrl = _cfg.BaseUrl?.TrimEnd('/') ?? "https://2factor.in";
            var url = $"{baseUrl}/API/V1/{apiKey}/SMS/VERIFY/{sessionId}/{otp}";

            var resp = await _http.GetAsync(url);
            var content = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                // treat as not verified
                return false;
            }

            // Example success JSON:
            // {"Status":"Success","Details":"OTP Matched"}
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("Status", out var s) && s.GetString() == "Success")
                    return true;

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
