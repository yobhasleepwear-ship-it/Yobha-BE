using MongoDB.Driver;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class CartRepository : ICartRepository
    {
        private readonly IMongoCollection<CartItem> _col;

        public CartRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<CartItem>("Cart");
        }

        public async Task<IEnumerable<CartItem>> GetForUserAsync(string userId)
        {
            return await _col.Find(c => c.UserId == userId).ToListAsync();
        }

        public async Task AddOrUpdateAsync(string userId, string productId, string variantSku, int quantity)
        {
            var existing = await _col.Find(c => c.UserId == userId && c.ProductId == productId && c.VariantSku == variantSku).FirstOrDefaultAsync();
            if (existing != null)
            {
                var update = Builders<CartItem>.Update.Set(c => c.Quantity, quantity);
                await _col.UpdateOneAsync(c => c.Id == existing.Id, update);
            }
            else
            {
                var item = new CartItem { UserId = userId, ProductId = productId, VariantSku = variantSku, Quantity = quantity };
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
