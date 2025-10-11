namespace ShoppingPlatform.DTOs
{
    public class SendOtpDto
    {
        public string PhoneNumber { get; set; } = null!;
    }

    public class ProviderResult
    {
        public bool IsSuccess { get; set; }
        public string SessionId { get; set; } = string.Empty;         // 2factor "Details" (session id) or generated id
        public string ProviderStatus { get; set; } = string.Empty;    // raw provider status (e.g., "Success", "Failed", "REJECTED")
        public string ProviderMessageId { get; set; } = string.Empty; // provider message id if returned
        public string RawResponse { get; set; } = string.Empty;       // raw body for debugging
    }
}
