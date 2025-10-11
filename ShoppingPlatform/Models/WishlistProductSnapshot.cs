using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class WishlistProductSnapshot
    {
        public string ProductId { get; set; } = string.Empty;        // PID...
        public string ProductObjectId { get; set; } = string.Empty;  // mongo _id
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? VariantSku { get; set; }
        public string? VariantId { get; set; }
        public string? VariantSize { get; set; }
        public string? VariantColor { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal UnitPrice { get; set; } = 0m;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? CompareAtPrice { get; set; }

        public string Currency { get; set; } = "INR";

        public bool IsActive { get; set; } = true;
        public bool FreeShipping { get; set; } = false;

        // optional price tiers / country prices if you want to display
        public List<string>? Tags { get; set; }
    }
}
