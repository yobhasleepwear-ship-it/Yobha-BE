using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Services
{
    public interface ISmsGatewayService
    {
        Task<(SmsProviderResult providerResult, string otp)> SendOtpAsync(string phoneNumber, CancellationToken ct = default);
    }
}
