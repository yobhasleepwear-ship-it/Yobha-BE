using MongoDB.Driver;
using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;
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

        // Get all cart items for a user
        public async Task<IEnumerable<CartItem>> GetForUserAsync(string userId)
        {
            var items = await _col.Find(c => c.UserId == userId).ToListAsync();
            return items;
        }

        // Add or update quantity for a product variant
        // productId = Product.ProductId (PIDxxxxx)
        public async Task AddOrUpdateAsync(string userId, string productId, string variantSku, int quantity)
        {
            if (string.IsNullOrWhiteSpace(productId))
                throw new ArgumentException("productId cannot be null or empty.");

            // Fetch the product (by readable PID)
            var product = await _productRepo.GetByProductIdAsync(productId);
            if (product == null)
                throw new KeyNotFoundException($"Product with ProductId {productId} not found.");

            // Check if item already exists in the user's cart
            var existing = await _col.Find(c =>
                c.UserId == userId &&
                c.ProductId == productId &&
                c.VariantSku == variantSku
            ).FirstOrDefaultAsync();

            if (existing != null)
            {
                var update = Builders<CartItem>.Update
                    .Set(c => c.Quantity, quantity)
                    .Set(c => c.UpdatedAt, DateTime.UtcNow);
                await _col.UpdateOneAsync(c => c.Id == existing.Id, update);
            }
            else
            {
                var item = new CartItem
                {
                    UserId = userId,
                    ProductId = product.ProductId,          // readable PID
                    ProductObjectId = product.Id,           // Mongo _id
                    ProductName = product.Name,
                    VariantSku = variantSku,
                    Quantity = quantity,
                    Price = product.Price,                  // optional, can override from variant later
                    AddedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _col.InsertOneAsync(item);
            }
        }

        public async Task RemoveAsync(string userId, string cartItemId)
        {
            await _col.DeleteOneAsync(c => c.UserId == userId && c.Id == cartItemId);
        }

        public async Task ClearAsync(string userId)
        {
            await _col.DeleteManyAsync(c => c.UserId == userId);
        }
    }
}
