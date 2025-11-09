using ShoppingPlatform.Models;
using System.ComponentModel.DataAnnotations;

namespace ShoppingPlatform.DTOs
{
    public class UpdateOrderStatusRequest
    {
        [Required]
        [StringLength(100)]
        public string Status { get; set; } = string.Empty;

        // Optional admin note that will be stored in audit/history or returned in response
        public string? Note { get; set; }

        // Optionally notify customer by email/sms when status updates
        public bool NotifyCustomer { get; set; } = true;
    }

    public class OrderStatusResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }          // admin user id or name
        public string? Note { get; set; }
    }

    public class CreateOrderRequest
    {
        public Address ShippingAddress { get; set; }
        public string PaymentMethod { get; set; } // "COD" or "razorpay"
        public string? CouponCode { get; set; }
        public decimal? LoyaltyDiscountAmount { get; set; }// points to amount calcualtions at frontend
    }

    public class CreateOrderRequestV2
    {
        public string Currency { get; set; } = string.Empty;
        public List<ProductRequest> productRequests { get; set; } = new();
        public Address? ShippingAddress { get; set; }
        public string PaymentMethod { get; set; } = "COD"; // "COD" or "razorpay"
        public string? CouponCode { get; set; }
        public decimal? CouponDiscount { get; set; }
        public decimal? LoyaltyDiscountAmount { get; set; } // points to amount calculations at frontend

        // optional gift card fields (if the client sends them)
        public string? GiftCardNumber { get; set; }
        public decimal? GiftCardAmount { get; set; }
        public string? ShippingRemarks { get; set; }
        public string? Email { get; set; }
        public string? orderCountry { get; set; }
        public bool? isGiftWrap { get; set; }
        public bool? delhiveryShipment { get; set; }
        public decimal? shippingPrice { get; set; }

    }
    public class ProductRequest
    { 
        public string id { get; set; }
        public string Size { get; set; }
        public int Quantity { get; set; }
        public List<string>? Fabric { get; set; }
        public List<string>? Color { get; set; }
        public string? Monogram { get; set; }

    }

    public class RazorpayOrderResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string RequestPayload { get; set; } = string.Empty;  // JSON sent to Razorpay
        public string ResponseBody { get; set; } = string.Empty;    // raw body returned from Razorpay
        public string? RazorpayOrderId { get; set; }                // the "id" from Razorpay if present
        public string? ErrorMessage { get; set; }                   // friendly error if any
    }

    public class CreateOrderResponse
    {
        public string Id { get; set; }    // add this line
        public bool Success { get; set; }
        public string OrderId { get; set; } = string.Empty;
        //public string OrderNumber { get; set; }
        public string? RazorpayOrderId { get; set; }
        public decimal Total { get; set; }
        // full debug object returned from the payment helper
        //public RazorpayOrderResult? RazorpayDebug { get; set; }
        // mirror what's saved in DB for quick inspection
        //public string? PaymentGatewayResponse { get; set; }
        public string? GiftCardNumber { get; set; }
    }

    public class UpdatePaymentStatusRequest
    {
        public string RazorpayOrderId { get; set; } = string.Empty;
        public string RazorpayPaymentId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; } = true;
    }

}
