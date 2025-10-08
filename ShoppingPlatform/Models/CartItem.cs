using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class CartItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string VariantSku { get; set; } = string.Empty;

        public int Quantity { get; set; } = 1;

        // 💰 Capture snapshot price at time of adding to cart
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => UnitPrice * Quantity;

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }

}
