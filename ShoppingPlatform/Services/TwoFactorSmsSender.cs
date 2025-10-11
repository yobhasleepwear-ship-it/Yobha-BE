// File: Services/TwoFactorSmsSender.cs
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        public TwoFactorSmsSender(
            TwoFactorService svc,
            IOptions<TwoFactorSettings> opts,
            IConfiguration configuration,
            ILogger<TwoFactorSmsSender> logger)
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
        }

        // Generate OTP and call TwoFactorService
        public async Task<ProviderResult> SendOtpAsync(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            // generate OTP
            var otp = new Random().Next(100000, 999999).ToString();

            // Normalize phone is handled by TwoFactorService, but we keep simple check
            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException(nameof(phoneNumber));

            _logger.LogInformation("Sending OTP to {phone} (masked)", phoneNumber);

            // var1 = optional name; var2 = OTP (important)
            var var1 = _cfg?.DefaultVar1 ?? "Customer";
            var var2 = otp;

            var result = await _svc.SendOtpAsync(_apiKey, phoneNumber, _cfg?.SenderId ?? "YOBHAS", _cfg?.TemplateName ?? "OTPSendTemplate1", var1, var2);

            // attach OTP to result.RawResponse? careful in prod (avoid storing OTP in logs)
            // Return session id (provider Details) in result.SessionId
            if (result.IsSuccess)
            {
                _logger.LogInformation("TwoFactor accepted OTP send. sessionId={sid}", result.SessionId);
            }
            else
            {
                _logger.LogWarning("TwoFactor rejected OTP send. status={status}", result.ProviderStatus);
            }

            // Optionally include otp in result for internal flows (NOT recommended for logs)
            // result.Otp = otp; // avoid persisting this

            return result;
        }

        public async Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("TwoFactor API key is not configured.");

            return await _svc.VerifyOtpAsync(_apiKey, sessionId, otp);
        }
    }
}
