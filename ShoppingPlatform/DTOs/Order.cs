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
        // Optional coupon code provided by client while creating order
        public string? CouponCode { get; set; }

        // Optionally you may include shipping method, notes etc later
        public string? Note { get; set; }
    }
}
