using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Services
{
    public interface ISmsSender
    {
        // Send OTP and get a session ID (used for verification)
        Task<ProviderResult> SendOtpAsync(string toPhoneNumber);

        // Verify OTP using sessionId and otp entered by user
        Task<bool> VerifyOtpAsync(string sessionId, string otp);
    }
}
