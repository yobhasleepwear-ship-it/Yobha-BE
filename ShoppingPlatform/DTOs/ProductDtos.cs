using ShoppingPlatform.Models;
using System.Collections.Generic;

namespace ShoppingPlatform.Dto
{
    public class ProductListItemDto
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string Category { get; set; } = null!;
        public List<string> Images { get; set; } = new();
        public bool Available { get; set; } = false;
    }

    public class ProductDetailDto
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public List<ProductImage> Images { get; set; } = new();
        public List<ProductVariant> Variants { get; set; } = new();
        public Dictionary<string, decimal> Prices { get; set; } = new();
        public List<string> Colors { get; set; } = new();        // added
        public List<string> VariantSkus { get; set; } = new();    // added
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
