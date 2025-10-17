using MongoDB.Driver;
using ShoppingPlatform.Models;
using ShoppingPlatform.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class CartRepository : ICartRepository
    {
        private readonly IMongoCollection<CartItem> _col;
        private readonly IProductRepository _productRepo;

        public CartRepository(IMongoDatabase db, IProductRepository productRepo)
        {
            _col = db.GetCollection<CartItem>("Cart");
            _productRepo = productRepo;
        }

        // Helper: map CartItem -> CartItemResponse
        //private CartItemResponse MapToDto(CartItem item)
        //{
        //    var dto = new CartItemResponse
        //    {
        //        Id = item.Id,
        //        UserId = item.UserId,
        //        Product = new CartProductSnapshot
        //        {
        //            ProductId = item.Snapshot.ProductId,
        //            ProductObjectId = item.Snapshot.ProductObjectId,
        //            Name = item.Snapshot.Name,
        //            Slug = item.Snapshot.Slug,
        //            ThumbnailUrl = item.Snapshot.ThumbnailUrl,
        //            VariantSku = item.Snapshot.VariantSku,
        //            VariantId = item.Snapshot.VariantId,
        //            VariantSize = item.Snapshot.VariantSize,
        //            VariantColor = item.Snapshot.VariantColor,
        //            UnitPrice = item.Snapshot.UnitPrice,
        //            CompareAtPrice = item.Snapshot.CompareAtPrice,
        //            Currency = item.Snapshot.Currency,
        //            StockQuantity = item.Snapshot.StockQuantity,
        //            ReservedQuantity = item.Snapshot.ReservedQuantity,
        //            IsActive = item.Snapshot.IsActive,
        //            FreeShipping = item.Snapshot.FreeShipping,
        //            CashOnDelivery = item.Snapshot.CashOnDelivery,
        //            PriceList = item.Snapshot.PriceList
        //        },
        //        Quantity = item.Quantity,
        //        AddedAt = item.AddedAt,
        //        UpdatedAt = item.UpdatedAt,
        //        Note = item.Note
        //    };

        //    return new CartItemResponse
        //    {
        //        Id = dto.Id,
        //        UserId = dto.UserId,
        //        Product = new ShoppingPlatform.DTOs.CartProductSnapshot
        //        {
        //            ProductId = dto.Product.ProductId,
        //            ProductObjectId = dto.Product.ProductObjectId,
        //            Name = dto.Product.Name,
        //            Slug = dto.Product.Slug,
        //            ThumbnailUrl = dto.Product.ThumbnailUrl,
        //            VariantSku = dto.Product.VariantSku,
        //            VariantId = dto.Product.VariantId,
        //            VariantSize = dto.Product.VariantSize,
        //            VariantColor = dto.Product.VariantColor,
        //            UnitPrice = dto.Product.UnitPrice,
        //            CompareAtPrice = dto.Product.CompareAtPrice,
        //            Currency = dto.Product.Currency,
        //            StockQuantity = dto.Product.StockQuantity,
        //            ReservedQuantity = dto.Product.ReservedQuantity,
        //            IsActive = dto.Product.IsActive,
        //            FreeShipping = dto.Product.FreeShipping,
        //            CashOnDelivery = dto.Product.CashOnDelivery,
        //            PriceList = dto.Product.PriceList
        //        },
        //        Quantity = dto.Quantity,
        //        AddedAt = dto.AddedAt,
        //        UpdatedAt = dto.UpdatedAt,
        //        Note = dto.Note
        //    };
        //}

        // Get for user (DTO)

        public async Task<CartResponse> GetForUserDtoAsync(string userId)
        {
            var items = await _col.Find(c => c.UserId == userId).ToListAsync();

            var itemDtos = new List<CartItemResponse>();
            decimal subtotal = 0m;
            foreach (var it in items)
            {
                var dto = MapToDto(it);
                itemDtos.Add(dto);
                subtotal += dto.Product.UnitPrice * dto.Quantity;
            }

            var summary = new ShoppingPlatform.DTOs.CartSummary
            {
                TotalItems = itemDtos.Sum(i => i.Quantity),
                DistinctItems = itemDtos.Count,
                SubTotal = decimal.Round(subtotal, 2),
                Shipping = 0m,
                Tax = 0m,
                Discount = 0m,
                GrandTotal = decimal.Round(subtotal, 2),
                Currency = itemDtos.FirstOrDefault()?.Product.Currency ?? "INR"
            };

            return new ShoppingPlatform.DTOs.CartResponse
            {
                Items = itemDtos,
                Summary = summary
            };
        }

        // Add or update. Returns the saved DTO.
        public async Task<CartItemResponse> AddOrUpdateAsync(string userId, string productId, string? size, int quantity, string? currency, string? note = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("productId cannot be null or empty.");

            var product = await _productRepo.GetByProductIdAsync(productId);
            if (product == null)
                throw new KeyNotFoundException($"Product with ProductId {productId} not found.");

            string currencyToUse = string.IsNullOrWhiteSpace(currency) ? "INR" : currency!;

            // --- Resolve PriceList by Size + Currency ---
            ShoppingPlatform.Models.Price? matchedTier = null;
            if (product.PriceList?.Any() == true)
            {
                matchedTier = product.PriceList
                    .FirstOrDefault(p => string.Equals(p.Size, size ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(p.Currency ?? "INR", currencyToUse, StringComparison.OrdinalIgnoreCase));
                if (matchedTier == null)
                {
                    matchedTier = product.PriceList
                        .FirstOrDefault(p => string.Equals(p.Size, size ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                }
            }


            decimal unitPrice;
            int availableStock;

            if (matchedTier != null)
            {
                unitPrice = matchedTier.PriceAmount;
                availableStock = matchedTier.Quantity;
            }
            else
            {
                // fallback: use product-level price & stock
                unitPrice = product.Price;
                availableStock = product.Stock;
            }

            // Country pricing - if currency differs and CountryPrices exist, prefer it (for shipping / suggestion)
            CountryPrice? suggestedCountryPrice = null;
            if (product.CountryPrices?.Any() == true)
            {
                suggestedCountryPrice = product.CountryPrices
                    .FirstOrDefault(cp => string.Equals(cp.Currency ?? "INR", currencyToUse, StringComparison.OrdinalIgnoreCase));
                if (suggestedCountryPrice != null)
                {
                    unitPrice = suggestedCountryPrice.PriceAmount;
                }
            }

            // Currency consistency check against existing cart items
            var existingCartItem = await _col.Find(c => c.UserId == userId).SortBy(c => c.AddedAt).FirstOrDefaultAsync();
            if (existingCartItem != null)
            {
                var existingCurrency = string.IsNullOrWhiteSpace(existingCartItem.Currency) ? "INR" : existingCartItem.Currency;
                if (!string.Equals(existingCurrency, currencyToUse, StringComparison.OrdinalIgnoreCase))
                {
                    return new CartItemResponse
                    {
                        Success = false,
                        Message = $"Currency mismatch: your cart currently contains items in '{existingCurrency}'. This product is in '{currencyToUse}'.",
                        SuggestedCountryPrice = suggestedCountryPrice
                    };
                }
            }

            // Build snapshot using matchedTier / product fallback
            var snapshot = new CartProductSnapshot
            {
                ProductId = product.ProductId,
                ProductObjectId = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                ThumbnailUrl = product.Images?.FirstOrDefault()?.Url,
                VariantSku = size,             // store chosen size into VariantSku for backwards compat
                VariantId = matchedTier?.Id,
                VariantSize = size,
                UnitPrice = Math.Round(unitPrice, 2),
                CompareAtPrice = product.CompareAtPrice,
                Currency = currencyToUse,
                StockQuantity = availableStock,
                ReservedQuantity = 0,
                IsActive = product.IsActive,
                FreeShipping = product.FreeDelivery || product.ShippingInfo?.FreeShipping == true,
                CashOnDelivery = product.ShippingInfo?.CashOnDelivery ?? false,
                PriceList = product.PriceList?.Select(p => new ShoppingPlatform.DTOs.PriceTier
                {
                    Id = p.Id,
                    Size = p.Size,
                    PriceAmount = p.PriceAmount,
                    Quantity = p.Quantity,
                    Currency = p.Currency
                }).ToList(),
                countryPrice = suggestedCountryPrice,
                Size = size
            };

            // use size stored in VariantSku to identify item in cart collection
            var skuToMatch = size ?? string.Empty;

            var existing = await _col.Find(c => c.UserId == userId && c.ProductId == productId && c.VariantSku == skuToMatch).FirstOrDefaultAsync();

            if (existing != null)
            {
                // If requested quantity > availableStock, still allow but warn (or reject - choose behavior)
                if (availableStock < quantity)
                {
                    // You may prefer to return failure instead of adding with smaller stock.
                    return new CartItemResponse
                    {
                        Success = false,
                        Message = $"Insufficient stock for {product.Name} ({skuToMatch}). Available: {availableStock}",
                        Product = snapshot
                    };
                }

                var update = Builders<CartItem>.Update
                    .Set(c => c.Quantity, quantity)
                    .Set(c => c.Price, unitPrice)
                    .Set(c => c.Currency, currencyToUse)
                    .Set(c => c.Snapshot, snapshot)
                    .Set(c => c.UpdatedAt, DateTime.UtcNow)
                    .Set(c => c.Note, note);

                await _col.UpdateOneAsync(c => c.Id == existing.Id, update);

                var updated = await _col.Find(c => c.Id == existing.Id).FirstOrDefaultAsync();
                return MapToDto(updated!);
            }
            else
            {
                // creation
                if (availableStock < quantity)
                {
                    return new CartItemResponse
                    {
                        Success = false,
                        Message = $"Insufficient stock for {product.Name} ({skuToMatch}). Available: {availableStock}",
                        Product = snapshot
                    };
                }

                var newItem = new CartItem
                {
                    UserId = userId,
                    ProductId = product.ProductId,
                    ProductObjectId = product.Id,
                    ProductName = product.Name,
                    VariantSku = skuToMatch,            // store size here for compatibility with Order flow
                    Quantity = quantity,
                    Price = unitPrice,
                    Currency = currencyToUse,
                    Snapshot = snapshot,
                    AddedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Note = note
                };

                await _col.InsertOneAsync(newItem);
                return MapToDto(newItem);
            }
        }

        // Map CartItem (DB model) to CartItemResponse DTO
        private CartItemResponse MapToDto(CartItem item)
        {
            return new CartItemResponse
            {
                Id = item.Id ?? string.Empty,
                UserId = item.UserId ?? string.Empty,
                Product = item.Snapshot ?? new CartProductSnapshot(),
                Quantity = item.Quantity,
                AddedAt = item.AddedAt,
                UpdatedAt = item.UpdatedAt,
                Note = item.Note,
                Success = true,
                Message = "OK"
            };
        }

        public async Task<CartItemResponse> UpdateQuantityAsync(string userId, string cartItemId, int quantity)
        {
            var existing = await _col.Find(c => c.UserId == userId && c.Id == cartItemId).FirstOrDefaultAsync();
            if (existing == null) throw new KeyNotFoundException("Cart item not found.");

            var update = Builders<CartItem>.Update
                .Set(c => c.Quantity, quantity)
                .Set(c => c.UpdatedAt, DateTime.UtcNow);

            await _col.UpdateOneAsync(c => c.Id == cartItemId, update);

            var updated = await _col.Find(c => c.Id == cartItemId).FirstOrDefaultAsync();
            return MapToDto(updated!);
        }

        public async Task RemoveAsync(string userId, string cartItemId)
        {
            await _col.DeleteOneAsync(c => c.UserId == userId && c.Id == cartItemId);
        }

        public async Task ClearAsync(string userId)
        {
            await _col.DeleteManyAsync(c => c.UserId == userId);
        }

        // inside CartRepository class
        public async Task<IEnumerable<CartItem>> GetForUserAsync(string userId)
        {
            return await _col.Find(c => c.UserId == userId).ToListAsync();
        }

    }
}