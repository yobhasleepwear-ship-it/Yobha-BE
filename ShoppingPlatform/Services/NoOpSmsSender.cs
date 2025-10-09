using ShoppingPlatform.Services;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Services
{
    // Simple dev fallback: logs and returns deterministic session id
    public class NoOpSmsSender : ISmsSender
    {
        public Task<string> SendOtpAsync(string phoneNumber)
        {
            // Create a fake sessionid; in dev you might want to return the OTP so you can test.
            var sessionId = Guid.NewGuid().ToString();
            Console.WriteLine($"[NoOpSmsSender] Simulated SendOtp for {phoneNumber}. SessionId={sessionId}");
            return Task.FromResult(sessionId);
        }

        public Task<bool> VerifyOtpAsync(string sessionId, string otp)
        {
            // By default fail verification to keep tests explicit.
            Console.WriteLine($"[NoOpSmsSender] Simulated VerifyOtp SessionId={sessionId}, OTP={otp} -> false");
            return Task.FromResult(false);
        }
    }
}
