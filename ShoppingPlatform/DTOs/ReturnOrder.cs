using ShoppingPlatform.Models;

namespace ShoppingPlatform.DTOs
{
    public class ReturnItemDto
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductObjectId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? ReasonForReturn { get; set; }
    }

    public class CreateReturnRequestDto
    {
        public string OrderNumber { get; set; } = string.Empty;
        public List<ReturnItemDto> Items { get; set; } = new();
        public List<string>? ReturnImagesURLs { get; set; }
        public string? ReturnReason { get; set; }
    }

    public class UpdateReturnRequestDto
    {
        public List<string>? ReturnImagesURLs { get; set; }
        public string? ReturnReason { get; set; }
    }

    public class ReturnResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string ReturnOrderNumber { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<ShoppingPlatform.Models.OrderItem>? Items { get; set; }
        public decimal? RefundAmount { get; set; }
        public string? RefundStatus { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AdminApproveDto
    {
        public string? AdminRemarks { get; set; }
        public bool UseInstantRefund { get; set; } = true;
    }

    public class AdminRejectDto
    {
        public string? AdminRemarks { get; set; }
    }

    public class AdminUpdateDto
    {
        public string? AdminRemarks { get; set; }
        public string? Status { get; set; } // e.g., Approved, Rejected, Processing, Completed
    }

    public class RefundResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? RefundId { get; set; }
        public string? PaymentId { get; set; }
        public decimal? Amount { get; set; } // in rupees
        public string? Status { get; set; }  // e.g. "created", "processed"
        public string? RawResponse { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
