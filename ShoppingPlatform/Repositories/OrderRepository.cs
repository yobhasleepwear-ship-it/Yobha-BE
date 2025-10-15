using MongoDB.Driver;
using ShoppingPlatform.Dto;
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
        public async Task<bool> UpdateAsync(string id, Order order)
        {
            order.UpdatedAt = DateTime.UtcNow;
            var result = await _col.ReplaceOneAsync(o => o.Id == id, order);
            return result.ModifiedCount > 0;
        }

        public async Task<PagedResult<Order>> GetOrdersAdminAsync(
    int page, int pageSize, string sort, OrderFilter filter, CancellationToken ct)
        {
            var builder = Builders<Order>.Filter;
            var mongoFilter = builder.Empty;

            // 🔹 Filter by OrderId if provided
            if (!string.IsNullOrEmpty(filter.Id))
                mongoFilter &= builder.Eq(o => o.Id, filter.Id);

            // 🔹 Filter by CreatedAt date range
            if (filter.From.HasValue)
                mongoFilter &= builder.Gte(o => o.CreatedAt, filter.From.Value);

            if (filter.To.HasValue)
                mongoFilter &= builder.Lte(o => o.CreatedAt, filter.To.Value);

            // 🔹 Sorting options
            var sortDef = sort switch
            {
                "createdAt_asc" => Builders<Order>.Sort.Ascending(o => o.CreatedAt),
                "total_desc" => Builders<Order>.Sort.Descending(o => o.Total),
                _ => Builders<Order>.Sort.Descending(o => o.CreatedAt)
            };

            // 🔹 Total record count
            var totalRecords = await _col.CountDocumentsAsync(mongoFilter, cancellationToken: ct);

            // 🔹 Apply pagination
            var items = await _col.Find(mongoFilter)
                                 .Sort(sortDef)
                                 .Skip((page - 1) * pageSize)
                                 .Limit(pageSize)
                                 .ToListAsync(ct);

            // 🔹 Compute total pages
            var totalPages = pageSize > 0
                ? (int)Math.Ceiling((double)totalRecords / pageSize)
                : 0;

            // 🔹 Return paged result with all metadata
            return new PagedResult<Order>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = (int)totalRecords,
            };
        }

        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var filter = Builders<Order>.Filter.Eq(o => o.Id, id);
            var result = await _col.DeleteOneAsync(filter);
            return result.DeletedCount == 1;
        }

    }
}
