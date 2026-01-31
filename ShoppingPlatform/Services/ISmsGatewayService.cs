using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Services
{
    public interface ISmsGatewayService
    {
        Task<(SmsProviderResult providerResult, string otp)> SendOtpAsync(string phoneNumber, CancellationToken ct = default);

        Task<SmsProviderResult> SendOrderConfirmationSmsAsync(string phoneNumber, string customerName, string orderId, string amount, CancellationToken ct = default);
    }
}
