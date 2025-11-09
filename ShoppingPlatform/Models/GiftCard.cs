using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class GiftCard
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Human-friendly unique code shown to user (e.g. GC202511AB12)
        public string GiftCardNumber { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Balance { get; set; } = 0m;

        public string Currency { get; set; } = "INR";

        public bool IsActive { get; set; } = true; // can be revoked
        public bool IsIssued { get; set; } = false; // set true when created
        public bool IsRedeemedFully => Balance <= 0m;

        public string IssuedOrderId { get; set; } = string.Empty; // link to order that created it (if any)
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RedeemedAt { get; set; }

        public string? OwnerUserId { get; set; } // optional: who purchased / who owns it
        public string? Notes { get; set; } // optional meta
    }
}
