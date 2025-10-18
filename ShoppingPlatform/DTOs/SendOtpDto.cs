namespace ShoppingPlatform.DTOs
{
    public class SendOtpDto
    {
        public string PhoneNumber { get; set; } = null!;
    }
    public class ProviderResult
    {
        public bool IsSuccess { get; set; }
        public string? ProviderStatus { get; set; }
        public string? RawResponse { get; set; }
        public string? SessionId { get; set; }
        public string? ProviderMessageId { get; set; }
    }

}
