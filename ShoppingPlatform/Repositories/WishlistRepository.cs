using MongoDB.Driver;
using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Repositories
{
    public class WishlistRepository : IWishlistRepository
    {
        private readonly IMongoCollection<Wishlist> _col;
        private readonly IProductRepository _productRepo;

        public WishlistRepository(IMongoDatabase db, IProductRepository productRepo)
        {
            _col = db.GetCollection<Wishlist>("Wishlists");
            _productRepo = productRepo;
        }

        public async Task<IEnumerable<Wishlist>> GetForUserAsync(string userId)
        {
            return await _col.Find(w => w.UserId == userId).ToListAsync();
        }

        public async Task<IEnumerable<WishlistItemResponse>> GetForUserDtoAsync(string userId)
        {
            var items = await _col.Find(w => w.UserId == userId).ToListAsync();
            return items.Select(MapToDto).ToList();
        }

        /// <summary>
        /// Adds or updates a wishlist entry. Returns the created/updated DTO.
        /// </summary>
        public async Task<WishlistItemResponse> AddAsync(
            string userId,
            string productId,
            string? variantSku = null,
            int desiredQuantity = 1,
            string? desiredSize = null,
            string? desiredColor = null,
            bool notify = true,
            string? note = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("productId cannot be empty");

            // compute sku match outside expression
            var skuToMatch = variantSku ?? string.Empty;

            // check existing
            var existing = await _col
                .Find(w => w.UserId == userId && w.ProductId == productId && (w.Snapshot.VariantSku ?? string.Empty) == skuToMatch)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                var update = Builders<Wishlist>.Update
                    .Set(w => w.DesiredQuantity, desiredQuantity)
                    .Set(w => w.DesiredSize, desiredSize)
                    .Set(w => w.DesiredColor, desiredColor)
                    .Set(w => w.NotifyWhenBackInStock, notify)
                    .Set(w => w.Note, note)
                    .Set(w => w.UpdatedAt, DateTime.UtcNow);

                await _col.UpdateOneAsync(w => w.Id == existing.Id, update);

                var updated = await _col.Find(w => w.Id == existing.Id).FirstOrDefaultAsync();
                if (updated == null) throw new InvalidOperationException("Wishlist updated but could not be read back.");
                return MapToDto(updated);
            }

            // fetch product and build snapshot
            var product = await _productRepo.GetByProductIdAsync(productId);
            if (product == null)
                throw new KeyNotFoundException($"Product {productId} not found");

            ProductVariant? variant = null;
            if (!string.IsNullOrWhiteSpace(variantSku) && product.Variants != null)
                variant = product.Variants.FirstOrDefault(v => v.Sku == variantSku);

            decimal unitPrice = variant?.PriceOverride ?? product.Price;

            var snapshot = new WishlistProductSnapshot
            {
                ProductId = product.ProductId,
                ProductObjectId = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                ThumbnailUrl = variant?.Images?.FirstOrDefault()?.Url ?? product.Images?.FirstOrDefault()?.Url,
                VariantSku = variant?.Sku ?? variantSku,
                VariantId = variant?.Id,
                VariantSize = variant?.Size,
                VariantColor = variant?.Color,
                UnitPrice = unitPrice,
                CompareAtPrice = product.CompareAtPrice,
                Currency = "INR",
                IsActive = product.IsActive,
                FreeShipping = product.FreeDelivery
            };

            var entry = new Wishlist
            {
                UserId = userId,
                ProductId = product.ProductId,
                Snapshot = snapshot,
                DesiredQuantity = desiredQuantity,
                DesiredSize = desiredSize,
                DesiredColor = desiredColor,
                NotifyWhenBackInStock = notify,
                Note = note,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _col.InsertOneAsync(entry);

            // map inserted doc to DTO and return
            return MapToDto(entry);
        }

        /// <summary>
        /// Remove by productId for a user. Returns true if a doc was deleted.
        /// </summary>
        public async Task<bool> RemoveAsync(string userId, string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
                return false;

            var result = await _col.DeleteOneAsync(w => w.UserId == userId && w.ProductId == productId);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Remove by wishlist document id for a user. Returns true if a doc was deleted.
        /// </summary>
        public async Task<bool> RemoveByIdAsync(string userId, string wishlistId)
        {
            if (string.IsNullOrWhiteSpace(wishlistId))
                return false;

            var result = await _col.DeleteOneAsync(w => w.UserId == userId && w.Id == wishlistId);
            return result.DeletedCount > 0;
        }

        // -------------------------
        // Helpers
        // -------------------------
        private WishlistItemResponse MapToDto(Wishlist w)
        {
            return new WishlistItemResponse
            {
                Id = w.Id,
                UserId = w.UserId,
                Product = new WishlistProductDto
                {
                    ProductId = w.Snapshot.ProductId,
                    ProductObjectId = w.Snapshot.ProductObjectId,
                    Name = w.Snapshot.Name,
                    Slug = w.Snapshot.Slug,
                    ThumbnailUrl = w.Snapshot.ThumbnailUrl,
                    VariantSku = w.Snapshot.VariantSku,
                    VariantId = w.Snapshot.VariantId,
                    VariantSize = w.Snapshot.VariantSize,
                    VariantColor = w.Snapshot.VariantColor,
                    UnitPrice = w.Snapshot.UnitPrice,
                    CompareAtPrice = w.Snapshot.CompareAtPrice ?? 0m,
                    Currency = w.Snapshot.Currency,
                    IsActive = w.Snapshot.IsActive,
                    FreeShipping = w.Snapshot.FreeShipping
                },
                DesiredQuantity = w.DesiredQuantity,
                DesiredSize = w.DesiredSize,
                DesiredColor = w.DesiredColor,
                NotifyWhenBackInStock = w.NotifyWhenBackInStock,
                MovedToCart = w.MovedToCart,
                Note = w.Note,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt
            };
        }
    }
}
