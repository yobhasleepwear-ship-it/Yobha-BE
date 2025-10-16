using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class CouponUsage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string CouponId { get; set; } = null!;
        public string UserId { get; set; } = null!;
        public string? OrderId { get; set; } = null!;
        public decimal DiscountAmount { get; set; } 
        public DateTime UsedAt { get; set; } = DateTime.UtcNow;
    }
}
