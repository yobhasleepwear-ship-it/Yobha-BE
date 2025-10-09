using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Sms;

namespace ShoppingPlatform.Services
{
    public class TwoFactorSmsSender : ISmsSender
    {
        private readonly TwoFactorService _svc;
        private readonly TwoFactorSettings _cfg;
        private readonly string _apiKey;

        public TwoFactorSmsSender(TwoFactorService svc, IOptions<TwoFactorSettings> opts)
        {
            _svc = svc;
            _cfg = opts.Value;
            // Support environment variable fallback
            _apiKey = _cfg?.ApiKey
                      ?? Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY")
                      ?? Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY")
                      ?? string.Empty;
        }

        public async Task<string> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            // Normalize phone number if needed (2factor expects country code; ensure the caller passes +91... or 91...).
            // Your AuthController passes the number; ensure format matches what 2factor expects.
            return await _svc.SendOtpAsync(_apiKey, phoneNumber, _cfg?.SenderId);
        }

        public async Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            return await _svc.VerifyOtpAsync(_apiKey, sessionId, otp);
        }
    }
}
