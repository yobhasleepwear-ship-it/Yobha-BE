using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class LoyaltyPointAudit
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [BsonRepresentation(BsonType.String)]
        public string UserId { get; set; } = string.Empty;

        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }

        public string Reason { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public string? RelatedEntityId { get; set; }

        [BsonRepresentation(BsonType.String)]
        public string? Operation { get; set; } //Credit or Debit

        public decimal Points { get; set; }

        public decimal? BalanceAfter { get; set; }

        public BsonDocument? Metadata { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class RecordResult
    {
        public bool Success { get; set; }
        public string? AuditId { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
