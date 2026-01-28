namespace ShoppingPlatform.DTOs
{
    public class UpdateOrderStatusAdmin
    {
        public string id { get; set; }
        public string type { get; set; }
        public string? orderStatus { get; set; }
        public string? paymentStatus { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public string? PaymentGatewayResponse { get; set; }
    }
}
