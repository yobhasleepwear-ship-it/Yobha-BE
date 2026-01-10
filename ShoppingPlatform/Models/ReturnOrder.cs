using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class ReturnOrder
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string ReturnOrderNumber { get; set; } = string.Empty; // REORD/YYYY/0001
        public string OrderNumber { get; set; } = string.Empty;       // original order number
        public string UserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


        // Pending | Approved | Rejected
        public string Status { get; set; } = "Pending";

        // snapshot of items user is returning (use same OrderItem model shape)
        public List<OrderItem> Items { get; set; } = new();

        public string? AdminOverallRemarks { get; set; }
        public bool IsProcessed { get; set; } = false; // whether refund/stock done

        public List<string> ReturnImagesURLs { get; set; } = new();
        public string? ReturnReason { get; set; }

        // --- Refund metadata (new)
        public string? RefundId { get; set; }           // Razorpay refund id (rfnd_...)
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? RefundAmount { get; set; }     // refunded amount (in rupees)
        public string? RefundStatus { get; set; }      // created | processed | failed | succeeded
        public DateTime? RefundCreatedAt { get; set; }
        public string? RefundPaymentId { get; set; }   // original Razorpay payment id (pay_...)
        public string? RefundResponseRaw { get; set; } // raw JSON from Razorpay for diagnostics
        public string? IdempotencyKey { get; set; }    // to guard against duplicate refund calls

        // Optional notes for audit
        public Dictionary<string, string>? Notes { get; set; } = new();

        // Admin who processed (optional)
        public string? ProcessedByAdminId { get; set; }
        public DateTime? ProcessedAt { get; set; }

        public DeliveryDetails? deliveryDetails { get; set; } = new DeliveryDetails();

    }
}
