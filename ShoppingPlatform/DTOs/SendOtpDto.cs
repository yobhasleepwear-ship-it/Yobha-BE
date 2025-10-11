namespace ShoppingPlatform.DTOs
{
    public class SendOtpDto
    {
        public string PhoneNumber { get; set; } = null!;
    }
    public class ProviderResult
    {
        public bool IsSuccess { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string ProviderStatus { get; set; } = string.Empty;
        public string ProviderMessageId { get; set; } = string.Empty;
        public string RawResponse { get; set; } = string.Empty;
        public string Reason => ProviderStatus ?? string.Empty;
    }
}
