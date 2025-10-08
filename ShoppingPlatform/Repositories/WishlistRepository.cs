using MongoDB.Driver;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class WishlistRepository : IWishlistRepository
    {
        private readonly IMongoCollection<Wishlist> _col;

        public WishlistRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<Wishlist>("Wishlists");
        }

        public async Task<IEnumerable<Wishlist>> GetForUserAsync(string userId)
        {
            return await _col.Find(w => w.UserId == userId).ToListAsync();
        }

        public async Task AddAsync(string userId, string productId)
        {
            var exists = await _col.Find(w => w.UserId == userId && w.ProductId == productId).FirstOrDefaultAsync();
            if (exists != null) return;

            var entry = new Wishlist { UserId = userId, ProductId = productId, CreatedAt = DateTime.UtcNow };
            await _col.InsertOneAsync(entry);
        }

        public async Task RemoveAsync(string userId, string productId)
        {
            await _col.DeleteOneAsync(w => w.UserId == userId && w.ProductId == productId);
        }
    }
}
