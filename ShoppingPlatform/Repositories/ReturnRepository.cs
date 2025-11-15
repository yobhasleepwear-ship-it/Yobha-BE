using MongoDB.Driver;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public class ReturnRepository : IReturnRepository
    {
        private readonly IMongoCollection<ReturnOrder> _collection;
        private readonly ILogger<ReturnRepository> _log;

        public ReturnRepository(IMongoDatabase database, ILogger<ReturnRepository> log, string collectionName = "returnorders")
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            _collection = database.GetCollection<ReturnOrder>(collectionName);
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<ReturnOrder> InsertAsync(ReturnOrder r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            await _collection.InsertOneAsync(r);
            return r;
        }

        public async Task<ReturnOrder?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var filter = Builders<ReturnOrder>.Filter.Eq(x => x.Id, id);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(ReturnOrder r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var filter = Builders<ReturnOrder>.Filter.Eq(x => x.Id, r.Id);
            var res = await _collection.ReplaceOneAsync(filter, r, new ReplaceOptions { IsUpsert = false });
            if (res.MatchedCount == 0)
            {
                _log.LogWarning("UpdateAsync: no document matched for id={Id}", r.Id);
            }
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var filter = Builders<ReturnOrder>.Filter.Eq(x => x.Id, id);
            await _collection.DeleteOneAsync(filter);
        }

        public async Task<List<ReturnOrder>> GetByUserIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return new List<ReturnOrder>();
            var filter = Builders<ReturnOrder>.Filter.Eq(x => x.UserId, userId);
            return await _collection.Find(filter).SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<List<ReturnOrder>> GetByOrderNumberAsync(string? orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber)) return new List<ReturnOrder>();
            var filter = Builders<ReturnOrder>.Filter.Eq(x => x.OrderNumber, orderNumber);
            return await _collection.Find(filter).SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<List<ReturnOrder>> GetAllAsync()
        {
            var filter = Builders<ReturnOrder>.Filter.Empty;
            return await _collection.Find(filter).SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        // Helper: create indexes. Call this at app startup (once).
        public async Task EnsureIndexesAsync(CancellationToken ct = default)
        {
            var keys = new List<CreateIndexModel<ReturnOrder>>
            {
                new CreateIndexModel<ReturnOrder>(Builders<ReturnOrder>.IndexKeys.Ascending(x => x.OrderNumber)),
                new CreateIndexModel<ReturnOrder>(Builders<ReturnOrder>.IndexKeys.Ascending(x => x.UserId)),
                new CreateIndexModel<ReturnOrder>(Builders<ReturnOrder>.IndexKeys.Descending(x => x.CreatedAt))
            };

            try
            {
                await _collection.Indexes.CreateManyAsync(keys, ct);
                _log.LogInformation("ReturnOrder indexes ensured.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error ensuring indexes on returnorders collection.");
            }
        }
    }
}
