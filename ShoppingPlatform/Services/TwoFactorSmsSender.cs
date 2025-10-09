using System;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<TwoFactorSmsSender> _logger;

        public TwoFactorSmsSender(TwoFactorService svc, IOptions<TwoFactorSettings> opts, IConfiguration configuration, ILogger<TwoFactorSmsSender> logger)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _cfg = opts?.Value ?? new TwoFactorSettings();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Prefer normal config section binding (TwoFactor:ApiKey)
            // Then try environment fallbacks (double underscore for section binding, older single underscore)
            var fromConfigSection = configuration["TwoFactor:ApiKey"];
            var fromEnvDoubleUnderscore = Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY");
            var fromEnvSingleUnderscore = Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY");

            _apiKey = !string.IsNullOrWhiteSpace(fromConfigSection) ? fromConfigSection
                    : !string.IsNullOrWhiteSpace(_cfg?.ApiKey) ? _cfg.ApiKey!
                    : !string.IsNullOrWhiteSpace(fromEnvDoubleUnderscore) ? fromEnvDoubleUnderscore
                    : fromEnvSingleUnderscore ?? string.Empty;

            // Log presence (not the value)
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("TwoFactor API key is not configured. Checked: TwoFactor:ApiKey, TwoFactorSettings.ApiKey, TWOFACTOR__APIKEY, TWOFACTOR_APIKEY.");
            }
            else
            {
                _logger.LogInformation("TwoFactor API key loaded (length={Length}).", _apiKey.Length);
            }
        }

        public async Task<string> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            // you may want to normalize phone number here (ensure country code)
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
