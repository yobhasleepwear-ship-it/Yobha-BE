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

            if (p.PriceList != null && p.PriceList.Count > 0)
            {
                var prices = p.PriceList
                    .Where(x => x != null)
                    .Where(x => string.IsNullOrWhiteSpace(country) || string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.PriceAmount)
                    .Where(v => v > 0)
                    .ToList();

                if (prices.Count > 0) return prices.Min();
            }

            //if (!string.IsNullOrWhiteSpace(country) && p.CountryPrices != null && p.CountryPrices.Count > 0)
            //{
            //    var match = p.CountryPrices.FirstOrDefault(cp => string.Equals(cp.Country?.Trim(), country.Trim(), StringComparison.OrdinalIgnoreCase));
            //    if (match != null && match.PriceAmount > 0) return match.PriceAmount;
            //}

            //if (p.CountryPrices != null && p.CountryPrices.Count > 0)
            //{
            //    var candidate = p.CountryPrices.Select(cp => cp.PriceAmount).Where(v => v > 0).DefaultIfEmpty(0m).Min();
            //    if (candidate > 0) return candidate;
            //}

            //if (p.Price > 0) return p.Price;

            return 0m;
        }

        public static ProductListItemDto ToListItemDto(Product p, string? country = null)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            var images = (p.Images ?? new List<ProductImage>()).Select(i => i?.Url).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

            // derive available colors / sizes from Inventory if present, otherwise fallback to Variants or SizeOfProduct
            var availableColors = new List<string>();
            var availableSizes = new List<string>();

            if (p.Inventory != null && p.Inventory.Count > 0)
            {
                availableColors = p.Inventory.Select(i => i.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                availableSizes = p.Inventory.Select(i => i.Size).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (p.Variants != null && p.Variants.Count > 0)
            {
                availableColors = p.Variants.Select(v => v.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                availableSizes = p.Variants.Select(v => v.Size).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (p.SizeOfProduct != null && p.SizeOfProduct.Count > 0)
            {
                availableSizes = p.SizeOfProduct.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            // availability: prefer inventory quantities, then PriceList qty, then variants qty, then fallback to stock
            var available = false;
            if (p.Inventory != null && p.Inventory.Any(i => i.Quantity - i.Reserved > 0)) available = true;
            else if (p.PriceList != null && p.PriceList.Any(pl => pl.Quantity > 0)) available = true;
            else if (p.Variants != null && p.Variants.Any(v => v.IsActive && v.Quantity > 0)) available = true;
            else if (p.Stock > 0) available = true;

            // Category: prefer productCategory, fallback to productMainCategory
            var category = !string.IsNullOrWhiteSpace(p.ProductCategory) ? p.ProductCategory : p.ProductMainCategory;

            return new ProductListItemDto
            {
                Id = p.Id ?? string.Empty,
                ProductId = p.ProductId ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(p.Name) ? (p.ProductMainCategory ?? string.Empty) : p.Name,
                Description = p.Description ?? string.Empty,
                Price = PickListPrice(p, country),
                Category = category ?? string.Empty,
                Images = images,
                Available = available,
                ProductMainCategory = p.ProductMainCategory ?? string.Empty,
                AvailableColors = availableColors,
                AvailableSizes = availableSizes,
                PriceList = p.PriceList
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

            if (p.PriceList != null && p.PriceList.Count > 0)
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

            if (!prices.Any() && p.CountryPrices != null && p.CountryPrices.Count > 0)
            {
                foreach (var cp in p.CountryPrices)
                {
                    if (string.IsNullOrWhiteSpace(cp.Country)) continue;
                    prices[cp.Country] = cp.PriceAmount;
                }
            }

            if (!prices.Any() && p.Price > 0)
                prices["default"] = p.Price;

            // Colors & sizes from inventory preferred
            var colors = (p.Inventory != null && p.Inventory.Count > 0)
                ? p.Inventory.Select(i => i.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : p.Variants?.Select(v => v.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                  ?? new List<string>();

            var sizes = (p.Inventory != null && p.Inventory.Count > 0)
                ? p.Inventory.Select(i => i.Size).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : p.SizeOfProduct ?? new List<string>();

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
                Colors = colors,
                VariantSkus = p.Variants?.Select(v => v.Sku).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>(),

                ProductMainCategory = p.ProductMainCategory ?? string.Empty,
                ProductCategory = !string.IsNullOrWhiteSpace(p.ProductCategory) ? p.ProductCategory : p.ProductMainCategory,
                ProductSubCategory = !string.IsNullOrWhiteSpace(p.ProductSubCategory) ? p.ProductSubCategory : p.ProductMainCategory,
                SizeOfProduct = sizes,
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
                UpdatedAt = p.UpdatedAt,

                Inventory = p.Inventory ?? new List<InventoryItem>()
            };

            return dto;
        }
    }
}
