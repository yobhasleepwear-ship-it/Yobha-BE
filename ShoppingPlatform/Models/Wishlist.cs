using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class Wishlist
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;

        // canonical readable product id (PID)
        public string ProductId { get; set; } = string.Empty;

        // snapshot of product details at the time of add
        public WishlistProductSnapshot Snapshot { get; set; } = new WishlistProductSnapshot();

        // optional: preferred quantity or size the user wants
        public int DesiredQuantity { get; set; } = 1;
        public string? DesiredSize { get; set; }
        public string? DesiredColor { get; set; }

        // flags & metadata
        public bool NotifyWhenBackInStock { get; set; } = true;
        public bool MovedToCart { get; set; } = false;
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
