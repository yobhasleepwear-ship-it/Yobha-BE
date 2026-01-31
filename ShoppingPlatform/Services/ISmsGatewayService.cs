using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Services
{
    public interface ISmsGatewayService
    {
        Task<(SmsProviderResult providerResult, string otp)> SendOtpAsync(string phoneNumber, CancellationToken ct = default);

        Task<SmsProviderResult> SendOrderConfirmationSmsAsync(string phoneNumber, string customerName, string orderId, string amount, CancellationToken ct = default);
        Task<SmsProviderResult> SendAdminNewOrderSmsAsync(
    string phoneNumber,
    string orderId,
    string customerName,
    string amount,
    CancellationToken ct = default);

        Task<SmsProviderResult> SendReturnInitiatedSmsAsync(
    string phoneNumber,
    string customerName,
    string orderId,
    CancellationToken ct = default);

        Task<SmsProviderResult> SendOrderCancellationSmsAsync(
    string phoneNumber,
    string customerName,
    string orderId,
    CancellationToken ct = default);
    }
}
