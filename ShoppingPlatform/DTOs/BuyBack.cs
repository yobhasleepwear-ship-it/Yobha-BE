namespace ShoppingPlatform.DTOs
{
    public class AdminUpdateBuybackRequest
    {
        public string BuybackId { get; set; }
        public string? NewStatus { get; set; }           // e.g. Approved
        public decimal? Amount { get; set; }                    // used for RepairReuse
        public decimal? LoyaltyPoints { get; set; }             // used for TradeIn/Recycle
        public string? Currency { get; set; }                   // e.g. "INR"
        public string? PaymentMethod { get; set; }              // "COD" or "razorpay"
        public string? Notes { get; set; }
    }

}
