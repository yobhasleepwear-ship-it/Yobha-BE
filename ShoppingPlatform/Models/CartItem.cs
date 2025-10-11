using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ShoppingPlatform.DTOs;
using System;

namespace ShoppingPlatform.Models
{
    public class CartItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;         // PID
        public string ProductObjectId { get; set; } = string.Empty;   // mongo _id
        public string ProductName { get; set; } = string.Empty;
        public string VariantSku { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; } = 0;                       // unit price snapshot

        public string Currency { get; set; } = "INR";

        public CartProductSnapshot Snapshot { get; set; } = new CartProductSnapshot();

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }
    }
}
