using System;
using System.Collections.Generic;
using System.Linq;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Helpers
{
    public static class ProductMappings
    {
        public static decimal PickListPrice(Product p, string? country = null)
        {
            if (p == null) return 0m;

            if (p.PriceList != null && p.PriceList.Any())
            {
                var prices = p.PriceList
                    .Where(x => x != null)
                    .Where(x => string.IsNullOrWhiteSpace(country) || string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.PriceAmount)
                    .Where(v => v > 0)
                    .ToList();

                if (prices.Any()) return prices.Min();
            }

            if (!string.IsNullOrWhiteSpace(country) && p.CountryPrices != null && p.CountryPrices.TryGetValue(country, out var cpVal) && cpVal > 0)
                return cpVal;

            if (p.CountryPrices != null && p.CountryPrices.Any())
            {
                var candidate = p.CountryPrices.Values.Where(v => v > 0).DefaultIfEmpty(0m).Min();
                if (candidate > 0) return candidate;
            }

            if (p.Price > 0) return p.Price;

            return 0m;
        }

        public static ProductListItemDto ToListItemDto(Product p, string? country = null)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            var images = (p.Images ?? new List<ProductImage>()).Select(i => i?.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

            return new ProductListItemDto
            {
                Id = p.Id ?? string.Empty,
                ProductId = p.ProductId ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(p.Name) ? p.ProductMainCategory ?? string.Empty : p.Name,
                Price = PickListPrice(p, country),
                Category = !string.IsNullOrWhiteSpace(p.ProductCategory) ? p.ProductCategory : p.Category,
                Images = images,
                Available = (p.Variants != null && p.Variants.Any(v => v.IsActive && v.Quantity > 0))
                            || (p.PriceList != null && p.PriceList.Any(pl => pl.Quantity > 0))
                            || p.Stock > 0,
                ProductMainCategory = p.ProductMainCategory ?? string.Empty
            };
        }

        public static List<ProductListItemDto> ToListItemDtos(IEnumerable<Product> products, string? country = null)
        {
            return (products ?? Enumerable.Empty<Product>()).Select(p => ToListItemDto(p, country)).ToList();
        }

        public static ProductDetailDto ToDetailDto(Product p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (p.PriceList != null && p.PriceList.Any())
            {
                var grouped = p.PriceList
                    .Where(x => x != null)
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Country) ? "default" : x.Country);

                foreach (var g in grouped)
                {
                    var min = g.Select(x => x.PriceAmount).Where(v => v > 0).DefaultIfEmpty(0m).Min();
                    prices[g.Key] = min;
                }
            }

            if (!prices.Any() && p.CountryPrices != null && p.CountryPrices.Any())
            {
                foreach (var kv in p.CountryPrices)
                    prices[kv.Key] = kv.Value;
            }

            if (!prices.Any() && p.Price > 0)
                prices["default"] = p.Price;

            var dto = new ProductDetailDto
            {
                Id = p.Id ?? string.Empty,
                ProductId = p.ProductId ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Slug = p.Slug ?? string.Empty,
                Description = p.Description ?? string.Empty,
                Images = p.Images ?? new List<ProductImage>(),
                Variants = p.Variants ?? new List<ProductVariant>(),
                Prices = prices,
                Colors = p.Variants?.Select(v => v.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList() ?? new List<string>(),
                VariantSkus = p.Variants?.Select(v => v.Sku).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(),

                ProductMainCategory = p.ProductMainCategory ?? string.Empty,
                ProductCategory = !string.IsNullOrWhiteSpace(p.ProductCategory) ? p.ProductCategory : p.Category,
                ProductSubCategory = !string.IsNullOrWhiteSpace(p.ProductSubCategory) ? p.ProductSubCategory : p.SubCategory,
                SizeOfProduct = p.SizeOfProduct ?? new List<string>(),
                FabricType = p.FabricType ?? new List<string>(),
                ProductVariationIds = p.ProductVariationIds ?? new List<string>(),

                AverageRating = p.AverageRating,
                ReviewCount = p.ReviewCount,
                Reviews = p.Reviews ?? new List<Review>(),

                IsFeatured = p.IsFeatured,
                SalesCount = p.SalesCount,
                IsActive = p.IsActive,
                IsDeleted = p.IsDeleted,

                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            };

            return dto;
        }
    }
}
