using MongoDB.Driver;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IMongoCollection<Order> _col;

        public OrderRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<Order>("Orders");
        }

        public async Task<IEnumerable<Order>> GetForUserAsync(string userId)
        {
            return await _col.Find(o => o.UserId == userId).SortByDescending(o => o.CreatedAt).ToListAsync();
        }

        public async Task<Order?> GetByIdAsync(string id)
        {
            return await _col.Find(o => o.Id == id).FirstOrDefaultAsync();
        }

        public async Task<Order> CreateAsync(Order order)
        {
            order.CreatedAt = DateTime.UtcNow;
            await _col.InsertOneAsync(order);
            return order;
        }

        public async Task<bool> UpdateStatusAsync(string id, string status)
        {
            var update = Builders<Order>.Update.Set(o => o.Status, status);
            var result = await _col.UpdateOneAsync(o => o.Id == id, update);
            return result.ModifiedCount > 0;
        }
    }
}
