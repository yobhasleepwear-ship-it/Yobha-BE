using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Models
{
    /// <summary>
    /// Main product document stored in MongoDB.
    /// Preserves existing fields and adds new fields:
    /// - CompareAtPrice, DiscountPercent
    /// - AvailableColors, SizeOfProduct
    /// - PriceList (per size/country)
    /// - Specifications, KeyFeatures, CareInstructions
    /// - Inventory (canonical source of truth for size+color qty)
    /// - ShippingInfo, SEO fields
    /// </summary>
    public class Product
    {
        // Mongo _id (ObjectId string)
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Human-friendly product id (PID2138282)
        [BsonElement("productId")]
        public string ProductId { get; set; } = string.Empty;

        // ---- existing fields (kept for backward compatibility) ----
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("slug")]
        public string Slug { get; set; } = string.Empty;

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        // legacy base price (fallback) — use Decimal128 for currency
        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; }

        // NEW: compare/strike-through price (used for discount badge)
        [BsonElement("compareAtPrice")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? CompareAtPrice { get; set; }

        // Derived/explicit discount percent (optional, can be computed)
        [BsonElement("discountPercent")]
        public int? DiscountPercent { get; set; }

        // convenience total stock (deprecated if Inventory used)
        [BsonElement("stock")]
        public int Stock { get; set; } = 0;

        // variants & images (existing)
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
        // New fields for the updated API / UI shape
        // ------------------------

        // Top-level storefront grouping (sleepwear, loungewear, etc.)
        [BsonElement("productMainCategory")]
        public string ProductMainCategory { get; set; } = string.Empty;

        // Secondary storefront category (women/men/kids)
        [BsonElement("productCategory")]
        public string ProductCategory { get; set; } = string.Empty;

        // Subcategory (e.g., "sleepwearSets")
        [BsonElement("productSubCategory")]
        public string ProductSubCategory { get; set; } = string.Empty;

        // Sizes available for UI rendering (XS/S/M etc.)
        [BsonElement("sizeOfProduct")]
        public List<string> SizeOfProduct { get; set; } = new();

        // Colors available for UI swatches (names or hex codes)
        [BsonElement("availableColors")]
        public List<string> AvailableColors { get; set; } = new();

        // Fabric types (used in specs/filter)
        [BsonElement("fabricType")]
        public List<string> FabricType { get; set; } = new();

        // Price list for per-size / per-country variants
        [BsonElement("priceList")]
        public List<Price> PriceList { get; set; } = new();

        // Links to other readable product ids that are variations
        [BsonElement("productVariationIds")]
        public List<string> ProductVariationIds { get; set; } = new();

        // Multi-region base prices list (replaces dictionary)
        [BsonElement("countryPrices")]
        public List<CountryPrice> CountryPrices { get; set; } = new();

        // Structured specifications (Details tab on product page)
        [BsonElement("specifications")]
        public ProductSpecifications? Specifications { get; set; }

        // Lists shown in the details tab
        [BsonElement("keyFeatures")]
        public List<string> KeyFeatures { get; set; } = new();

        [BsonElement("careInstructions")]
        public List<string> CareInstructions { get; set; } = new();

        // ------------------------
        // Inventory: canonical source-of-truth for stock per (size, color)
        // ------------------------
        // Use Inventory for availability checks and reservations.
        [BsonElement("inventory")]
        public List<InventoryItem> Inventory { get; set; } = new();

        // Shipping & policy metadata (shown as badges on UI)
        [BsonElement("freeDelivery")]
        public bool FreeDelivery { get; set; } = false;

        [BsonElement("returnPolicy")]
        public string? ReturnPolicy { get; set; } // e.g., "7 Days"

        [BsonElement("shippingInfo")]
        public ShippingInfo? ShippingInfo { get; set; }

        // SEO & storefront metadata
        [BsonElement("metaTitle")]
        public string? MetaTitle { get; set; }

        [BsonElement("metaDescription")]
        public string? MetaDescription { get; set; }

        // Analytics / counters
        [BsonElement("views")]
        public long Views { get; set; } = 0;

        [BsonElement("unitsSold")]
        public long UnitsSold { get; set; } = 0;
    }

    // ------------------------
    // Inventory item — canonical stock record keyed by (size, color) optionally per warehouse
    // ------------------------
    public class InventoryItem
    {
        [BsonRepresentation(BsonType.String)]
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        // Optional link to variant or SKU
        [BsonElement("variantId")]
        public string? VariantId { get; set; }

        [BsonElement("sku")]
        public string? Sku { get; set; }

        // Unique defining tuple for inventory: size + color
        [BsonElement("size")]
        public string Size { get; set; } = string.Empty;

        [BsonElement("color")]
        public string Color { get; set; } = string.Empty;

        // Available quantity for this tuple
        [BsonElement("quantity")]
        public int Quantity { get; set; } = 0;

        // Optional reserved qty (for cart reservation flows)
        [BsonElement("reserved")]
        public int Reserved { get; set; } = 0;

        // Optional warehouse/location level
        [BsonElement("warehouseId")]
        public string? WarehouseId { get; set; }

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ------------------------
    // Variant kept for backwards compatibility / UI mapping
    // ------------------------
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

        // Deprecated: keep for compatibility; prefer Inventory for actual stock checks
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

    // ------------------------
    // Media
    // ------------------------
    public class ProductImage
    {
        [BsonElement("url")]
        public string Url { get; set; } = string.Empty;

        [BsonElement("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [BsonElement("alt")]
        public string? Alt { get; set; }

        // Internal tracking who uploaded the image
        [BsonElement("uploadedByUserId")]
        public string? UploadedByUserId { get; set; }

        [BsonElement("uploadedAt")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    // ------------------------
    // Price record (used in PriceList)
    // ------------------------
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
        public string Currency { get; set; } = "INR";

        [BsonElement("quantity")]
        public int Quantity { get; set; } = 0;

        [BsonElement("country")]
        public string Country { get; set; } = string.Empty;
    }

    // ------------------------
    // Country price record (replaces countryPrices dictionary)
    // ------------------------
    public class CountryPrice
    {
        [BsonRepresentation(BsonType.String)]
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("country")]
        public string Country { get; set; } = string.Empty;

        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal PriceAmount { get; set; }

        [BsonElement("currency")]
        public string Currency { get; set; } = "INR";
    }

    // ------------------------
    // Reviews
    // ------------------------
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

    // ------------------------
    // Structured product specifications (Details tab)
    // ------------------------
    public class ProductSpecifications
    {
        [BsonElement("fabric")]
        public string? Fabric { get; set; }

        [BsonElement("length")]
        public string? Length { get; set; }

        [BsonElement("origin")]
        public string? Origin { get; set; }

        [BsonElement("fit")]
        public string? Fit { get; set; }

        [BsonElement("care")]
        public string? Care { get; set; }

        // Flexible extra rows converted from dictionary to typed list
        [BsonElement("extra")]
        public List<SpecificationField>? Extra { get; set; }
    }

    // ------------------------
    // Specification field (replaces dictionary key/value)
    // ------------------------
    public class SpecificationField
    {
        [BsonRepresentation(BsonType.String)]
        [BsonElement("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("key")]
        public string Key { get; set; } = string.Empty;

        [BsonElement("value")]
        public string Value { get; set; } = string.Empty;
    }

    // ------------------------
    // Shipping
    // ------------------------
    public class ShippingInfo
    {
        [BsonElement("freeShipping")]
        public bool FreeShipping { get; set; } = false;

        [BsonElement("estimatedDelivery")]
        public string? EstimatedDelivery { get; set; } // e.g., "2-3 Days"

        [BsonElement("shippingPrice")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? ShippingPrice { get; set; }

        [BsonElement("cashOnDelivery")]
        public bool CashOnDelivery { get; set; } = false;
    }
}
