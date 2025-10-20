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
        public string Currency { get; set; }
        public List<ProductRequest> productRequests { get; set; }
        public Address ShippingAddress { get; set; }
        public string PaymentMethod { get; set; } // "COD" or "razorpay"
        public string? CouponCode { get; set; }
        public decimal? CouponDiscount { get; set; }

        public decimal? LoyaltyDiscountAmount { get; set; }// points to amount calcualtions at frontend
    }

    public class ProductRequest
    { 
        public string id { get; set; }
        public string Size { get; set; }
        public int Quantity { get; set; }

    }

}
