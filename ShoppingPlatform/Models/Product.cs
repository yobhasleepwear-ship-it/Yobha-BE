using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Models
{
    public class Product
    {
        // Mongo _id (ObjectId string)
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // ---- New readable product id (e.g., PID2138282) ----
        // This is the field you asked for: human-friendly, unique.
        // Use repository method ExistsByProductIdAsync when generating to guarantee uniqueness.
        [BsonElement("productId")]
        public string ProductId { get; set; } = string.Empty;

        // --- existing fields (kept for backward compatibility) ---
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("slug")]
        public string Slug { get; set; } = string.Empty;

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        // legacy base price (fallback)
        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; }

        // legacy taxonomy (kept)
        [BsonElement("category")]
        public string Category { get; set; } = string.Empty;

        [BsonElement("subCategory")]
        public string SubCategory { get; set; } = string.Empty;

        // convenience total stock (optional)
        [BsonElement("stock")]
        public int Stock { get; set; } = 0;

        // --- variants & images (existing) ---
        [BsonElement("variants")]
        public List<ProductVariant> Variants { get; set; } = new();

        [BsonElement("images")]
        public List<ProductImage> Images { get; set; } = new();

        [BsonElement("variantSkus")]
        public List<string> VariantSkus { get; set; } = new();

        // reviews (existing)
        [BsonElement("averageRating")]
        public double AverageRating { get; set; } = 0.0;

        [BsonElement("reviewCount")]
        public int ReviewCount { get; set; } = 0;

        [BsonElement("reviews")]
        public List<Review> Reviews { get; set; } = new();

        // flags
        [BsonElement("isFeatured")]
        public bool IsFeatured { get; set; } = false;

        [BsonElement("salesCount")]
        public int SalesCount { get; set; } = 0;

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;

        [BsonElement("isDeleted")]
        public bool IsDeleted { get; set; } = false;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


        // ------------------------
        // New fields for the updated API shape
        // ------------------------

        // Mandatory top-level product grouping (e.g., "sleepwear", "loungewear", "homeWear")
        [BsonElement("productMainCategory")]
        public string ProductMainCategory { get; set; } = string.Empty;

        // Secondary category level (e.g., "women", "men", "kids", "pets")
        [BsonElement("productCategory")]
        public string ProductCategory { get; set; } = string.Empty;

        // Sub-category (e.g., "sleepwearSets", "coordSets")
        [BsonElement("productSubCategory")]
        public string ProductSubCategory { get; set; } = string.Empty;

        // Explicit list of sizes available for this product (S/M/L etc.)
        [BsonElement("sizeOfProduct")]
        public List<string> SizeOfProduct { get; set; } = new();

        // Fabric types (cotton, silk, etc.)
        [BsonElement("fabricType")]
        public List<string> FabricType { get; set; } = new();

        // Price array: supports per-size, per-country price + quantity
        // Field name: priceList (keeps existing PriceList usage)
        [BsonElement("priceList")]
        public List<Price> PriceList { get; set; } = new();

        // Links to other products (variations) by product id — e.g. "Prod002" or readable PID
        [BsonElement("productVariationIds")]
        public List<string> ProductVariationIds { get; set; } = new();

        // Multi-region base prices map (kept for compatibility)
        [BsonElement("countryPrices")]
        public Dictionary<string, decimal> CountryPrices { get; set; } = new();
    }

    // Price subdocument
    public class Price
    {
        [BsonRepresentation(BsonType.String)]
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("size")]
        public string Size { get; set; } = string.Empty;

        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal PriceAmount { get; set; }

        [BsonElement("currency")]
        public string Currency { get; set; } = string.Empty;

        [BsonElement("quantity")]
        public int Quantity { get; set; } = 0;

        [BsonElement("country")]
        public string Country { get; set; } = string.Empty;
    }

    public class ProductVariant
    {
        [BsonRepresentation(BsonType.String)]
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("sku")]
        public string Sku { get; set; } = string.Empty;

        [BsonElement("color")]
        public string Color { get; set; } = string.Empty;

        [BsonElement("size")]
        public string Size { get; set; } = string.Empty;

        [BsonElement("quantity")]
        public int Quantity { get; set; } = 0;

        [BsonElement("priceOverride")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? PriceOverride { get; set; }

        [BsonElement("images")]
        public List<ProductImage>? Images { get; set; }

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;
    }

    public class ProductImage
    {
        [BsonElement("url")]
        public string Url { get; set; } = null!;

        [BsonElement("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [BsonElement("alt")]
        public string? Alt { get; set; }

        [BsonElement("uploadedByUserId")]
        public string UploadedByUserId { get; set; } = null!;

        [BsonElement("uploadedAt")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class Review
    {
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [BsonElement("userId")]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("rating")]
        public int Rating { get; set; }

        [BsonElement("comment")]
        public string Comment { get; set; } = string.Empty;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("approved")]
        public bool Approved { get; set; } = false;
    }
}
