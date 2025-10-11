using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
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

        public TwoFactorSmsSender(
            TwoFactorService svc,
            IOptions<TwoFactorSettings> opts,
            IConfiguration configuration,
            ILogger<TwoFactorSmsSender> logger)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _cfg = opts?.Value ?? new TwoFactorSettings();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load API key from multiple possible sources
            var fromConfig = configuration["TwoFactor:ApiKey"];
            var fromSettings = _cfg?.ApiKey;
            var fromEnvDouble = Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY");
            var fromEnvSingle = Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY");

            _apiKey = !string.IsNullOrWhiteSpace(fromConfig) ? fromConfig
                    : !string.IsNullOrWhiteSpace(fromSettings) ? fromSettings!
                    : !string.IsNullOrWhiteSpace(fromEnvDouble) ? fromEnvDouble
                    : fromEnvSingle ?? string.Empty;

            // Log presence
            if (string.IsNullOrWhiteSpace(_apiKey))
                _logger.LogError("TwoFactor API key not found in any source.");
            else
                _logger.LogInformation("TwoFactor API key loaded successfully (length={len})", _apiKey.Length);
        }

        public async Task<string> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            // Generate random OTP (DLT VAR2)
            var otp = new Random().Next(100000, 999999).ToString();

            _logger.LogInformation("Sending OTP via TwoFactor -> Phone={phone}, OTP={otpMasked}", phoneNumber, "******");

            try
            {
                // Use approved template (must match DLT)
                string sessionId = await _svc.SendOtpAsync(
                    _apiKey,
                    phoneNumber,
                    _cfg?.SenderId ?? "YOBHAS",
                    _cfg?.TemplateName ?? "OTPSendTemplate1",
                    var1: "Customer",
                    var2: otp
                );

                _logger.LogInformation("OTP sent successfully. SessionId={sessionId}", sessionId);
                return sessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP via 2Factor for {phone}", phoneNumber);
                throw;
            }
        }

        public async Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            try
            {
                bool success = await _svc.VerifyOtpAsync(_apiKey, sessionId, otp);
                _logger.LogInformation("OTP verification result={result} for Session={session}", success, sessionId);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify OTP for session {session}", sessionId);
                return false;
            }
        }
    }
}
