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

    public class SmsGatewayVerifyResponse1
    {
        // pattern used in HTTP API PDF (ErrorCode/ErrorMessage)
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public string JobId { get; set; }
        public object MessageData { get; set; }
    }

    public class SmsGatewayVerifyResponse2
    {
        // pattern mentioned in KB (statusCode etc)
        public int? statusCode { get; set; }
        public string status { get; set; }
        public string message { get; set; }
        public string transactionId { get; set; }
        public string expiryTime { get; set; }
    }

}
