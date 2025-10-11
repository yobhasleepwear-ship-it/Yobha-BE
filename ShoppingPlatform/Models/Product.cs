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

        public string ProductId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? CompareAtPrice { get; set; }

        public int? DiscountPercent { get; set; }
        public int Stock { get; set; } = 0;

        public List<ProductVariant> Variants { get; set; } = new();
        public List<ProductImage> Images { get; set; } = new();
        public List<string> VariantSkus { get; set; } = new();
        public double AverageRating { get; set; } = 0.0;
        public int ReviewCount { get; set; } = 0;
        public List<Review> Reviews { get; set; } = new();

        public bool IsFeatured { get; set; } = false;
        public int SalesCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Category hierarchy
        public string ProductMainCategory { get; set; } = string.Empty;
        public string ProductCategory { get; set; } = string.Empty;
        public string ProductSubCategory { get; set; } = string.Empty;

        // Attributes
        public List<string> SizeOfProduct { get; set; } = new();
        public List<string> AvailableColors { get; set; } = new();
        public List<string> FabricType { get; set; } = new();

        public List<Price> PriceList { get; set; } = new();
        public List<string> ProductVariationIds { get; set; } = new();
        public List<CountryPrice> CountryPrices { get; set; } = new();

        public ProductSpecifications? Specifications { get; set; }
        public List<string> KeyFeatures { get; set; } = new();
        public List<string> CareInstructions { get; set; } = new();

        public List<InventoryItem> Inventory { get; set; } = new();

        // Shipping & Policy
        public bool FreeDelivery { get; set; } = false;
        public string? ReturnPolicy { get; set; }
        public ShippingInfo? ShippingInfo { get; set; }

        // SEO
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }

        // Analytics
        public long Views { get; set; } = 0;
        public long UnitsSold { get; set; } = 0;
    }

    public class InventoryItem
    {
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string? VariantId { get; set; }
        public string? Sku { get; set; }
        public string Size { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int Quantity { get; set; } = 0;
        public int Reserved { get; set; } = 0;
        public string? WarehouseId { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProductVariant
    {
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Sku { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int Quantity { get; set; } = 0;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? PriceOverride { get; set; }

        public List<ProductImage>? Images { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ProductImage
    {
        public string Url { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Alt { get; set; }
        public string? UploadedByUserId { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class Price
    {
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Size { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal PriceAmount { get; set; }

        public string Currency { get; set; } = "INR";
        public int Quantity { get; set; } = 0;
        public string Country { get; set; } = string.Empty;
    }

    public class CountryPrice
    {
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Country { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal PriceAmount { get; set; }

        public string Currency { get; set; } = "INR";
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

    public class ProductSpecifications
    {
        public string? Fabric { get; set; }
        public string? Length { get; set; }
        public string? Origin { get; set; }
        public string? Fit { get; set; }
        public string? Care { get; set; }
        public List<SpecificationField>? Extra { get; set; }
    }

    public class SpecificationField
    {
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class ShippingInfo
    {
        public bool FreeShipping { get; set; } = false;
        public string? EstimatedDelivery { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? ShippingPrice { get; set; }

        public bool CashOnDelivery { get; set; } = false;
    }
}
