using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Dto
{
    // List item shown in product listing
    public class ProductListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public List<string> Images { get; set; } = new List<string>();
        public bool Available { get; set; } = false;
        public string ProductMainCategory { get; set; } = string.Empty;

        // NEW: expose available swatches / sizes for frontend
        public List<string> AvailableColors { get; set; } = new List<string>();
        public List<string> AvailableSizes { get; set; } = new List<string>();
        public List<Price> PriceList { get; set; } = new();
        public List<SuggestedProducts> SuggestedProducts { get; set; } = new();
        public bool IsActive { get; set; }

    }

    // Full product detail returned by GET /api/products/{id}
    public class ProductDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public List<ProductImage> Images { get; set; } = new List<ProductImage>();
        public List<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

        // Prices keyed by country or "default"
        public Dictionary<string, decimal> Prices { get; set; } = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        public List<string> Colors { get; set; } = new List<string>();
        public List<string> VariantSkus { get; set; } = new List<string>();

        public string ProductMainCategory { get; set; } = string.Empty;
        public string ProductCategory { get; set; } = string.Empty;
        public string ProductSubCategory { get; set; } = string.Empty;

        public List<string> SizeOfProduct { get; set; } = new List<string>();
        public List<string> FabricType { get; set; } = new List<string>();
        public List<string> ProductVariationIds { get; set; } = new List<string>();

        public double AverageRating { get; set; } = 0.0;
        public int ReviewCount { get; set; } = 0;
        public List<Review> Reviews { get; set; } = new List<Review>();

        public bool IsFeatured { get; set; } = false;
        public int SalesCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // NEW: include inventory details so frontend can know per (size,color) quantities
        public List<InventoryItem> Inventory { get; set; } = new List<InventoryItem>();
    }

    public class CategoryCount
    {
        public string Category { get; set; } = null!;
        public int Count { get; set; }
    }

    public class PagedResult<T>
    {
        public IEnumerable<T> Items { get; set; } = Array.Empty<T>();

        // Current page number (1-based)
        public int Page { get; set; } = 1;

        // Number of items per page
        public int PageSize { get; set; } = 10;

        // Total number of matching records in DB
        public long TotalCount { get; set; } = 0;

        // Computed total pages
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
