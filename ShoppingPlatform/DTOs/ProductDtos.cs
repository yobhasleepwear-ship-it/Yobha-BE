using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Dto
{
    // List item shown in product listing
    public class ProductListItemDto
    {
        public string Id { get; set; } = null!;
        public string ProductId { get; set; } = string.Empty;   // readable PID (new)
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string Category { get; set; } = null!;
        public List<string> Images { get; set; } = new();
        public bool Available { get; set; } = false;
        public string ProductMainCategory { get; set; } = string.Empty;
    }

    // Full product detail returned by GET /api/products/{id}
    public class ProductDetailDto
    {
        public string Id { get; set; } = null!;
        public string ProductId { get; set; } = string.Empty;   // readable PID included
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = string.Empty;
        public string Description { get; set; } = null!;
        public List<ProductImage> Images { get; set; } = new();
        public List<ProductVariant> Variants { get; set; } = new();
        public Dictionary<string, decimal> Prices { get; set; } = new();   // country -> price mapping
        public List<string> Colors { get; set; } = new();                 // distinct colors from variants
        public List<string> VariantSkus { get; set; } = new();            // skus from variants

        // NEW fields added to match updated Product model
        public string ProductMainCategory { get; set; } = string.Empty;
        public string ProductCategory { get; set; } = string.Empty;
        public string ProductSubCategory { get; set; } = string.Empty;
        public List<string> SizeOfProduct { get; set; } = new();
        public List<string> FabricType { get; set; } = new();
        public List<string> ProductVariationIds { get; set; } = new();

        // review & metadata fields
        public double AverageRating { get; set; } = 0.0;
        public int ReviewCount { get; set; } = 0;
        public List<Review> Reviews { get; set; } = new();

        // flags / admin fields
        public bool IsFeatured { get; set; } = false;
        public int SalesCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        // timestamps
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CategoryCount
    {
        public string Category { get; set; } = null!;
        public int Count { get; set; }
    }

    public class PagedResult<T>
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }
        public List<T> Items { get; set; } = new();
    }
}
