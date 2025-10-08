using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

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

        // Base price (fallback)
        public decimal Price { get; set; }

        public string Category { get; set; } = string.Empty;

        // Total stock can be the sum of variant quantities (optional convenience)
        public int Stock { get; set; } = 0;

        // Variant list: each entry is a specific (color, size) combination
        public List<ProductVariant> Variants { get; set; } = new();

        // Multi-region price map for base price; variant can override
        public Dictionary<string, decimal> CountryPrices { get; set; } = new();

        public bool IsFeatured { get; set; } = false;
        public int SalesCount { get; set; } = 0;

        public List<ProductImage> Images { get; set; } = new();
        public List<string> VariantSkus { get; set; } = new();

        // Reviews
        public double AverageRating { get; set; } = 0.0;
        public int ReviewCount { get; set; } = 0;
        public List<Review> Reviews { get; set; } = new();

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProductVariant
    {
        // unique id for this variant (string to be Mongo-safe)
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        // SKU is optional but recommended (unique across your catalog)
        public string Sku { get; set; } = string.Empty;

        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;

        // quantity available for this color+size
        public int Quantity { get; set; } = 0;

        // optional: price override for this variant (per-country override supported separately)
        public decimal? PriceOverride { get; set; }

        // optional images specific to this variant
        public List<ProductImage>? Images { get; set; }

        // active flag
        public bool IsActive { get; set; } = true;
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
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool Approved { get; set; } = false;
    }
}
