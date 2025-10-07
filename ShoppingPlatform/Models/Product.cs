using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class Product
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Stock { get; set; } = 0;
        public bool IsFeatured { get; set; } = false;
        public int SalesCount { get; set; } = 0;

        public List<ProductImage> Images { get; set; } = new();
        public List<string> VariantSkus { get; set; } = new();

        // aggregated
        public double AverageRating { get; set; } = 0.0;
        public int ReviewCount { get; set; } = 0;

        // keep reviews separate or embedded — embedded here for simplicity (varies by scale)
        public List<Review> Reviews { get; set; } = new();
    }

    public class ProductImage
    {
        public string Url { get; set; } = null!;
        public string? ThumbnailUrl { get; set; }
        public string? Alt { get; set; }
        public string UploadedByUserId { get; set; } = null!;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class Review
    {
        // Mongo-friendly string id for review
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // who wrote the review (user id)
        public string UserId { get; set; } = string.Empty;

        // rating 1..5
        public int Rating { get; set; }

        // optional comment
        public string Comment { get; set; } = string.Empty;

        // when submitted
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // moderation flag
        public bool Approved { get; set; } = false;
    }
}
