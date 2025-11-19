using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Services
{
    public class LoyaltyPointAuditService : ILoyaltyPointAuditService
    {
        public const string CollectionName = "loyaltyPointAudits";

        private readonly IMongoCollection<LoyaltyPointAudit> _collection;
        private const int MaxPageSize = 100;

        public LoyaltyPointAuditService(IMongoDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            _collection = database.GetCollection<LoyaltyPointAudit>(CollectionName);
        }

        public async Task<RecordResult> RecordAsync(LoyaltyPointAudit audit, CancellationToken cancellationToken = default)
        {
            if (audit == null) throw new ArgumentNullException(nameof(audit));
            try
            {
                await _collection.InsertOneAsync(audit, cancellationToken: cancellationToken);
                return new RecordResult { Success = true, AuditId = audit.Id };
            }
            catch (Exception ex)
            {
                // log ex as needed
                return new RecordResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<RecordResult> RecordSimpleAsync(
            string userId,
            string operation,
            decimal points,
            string reason,
            string? relatedEntityId = null,
            string? email = null,
            string? phone = null,
            decimal? balanceAfter = null,
            BsonDocument? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var audit = new LoyaltyPointAudit
            {
                UserId = userId,
                Operation = operation,
                Points = points,
                Reason = reason,
                RelatedEntityId = relatedEntityId,
                Email = email,
                PhoneNumber = phone,
                BalanceAfter = balanceAfter,
                Metadata = metadata,
                CreatedAt = DateTime.UtcNow
            };

            return await RecordAsync(audit, cancellationToken);
        }

        public async Task<PagedResult<LoyaltyPointAudit>> GetForUserAsync(
        string userId,
        int page = 1,
        int pageSize = 20
        )
        {
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException(nameof(userId));
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var filter = Builders<LoyaltyPointAudit>.Filter.Eq(a => a.UserId, userId);
            var sort = Builders<LoyaltyPointAudit>.Sort.Descending(a => a.CreatedAt);
            var skip = (page - 1) * pageSize;

            var countTask = _collection.CountDocumentsAsync(filter, null);
            var docsTask = _collection.Find(filter)
                                      .Sort(sort)
                                      .Skip(skip)
                                      .Limit(pageSize)
                                      .ToListAsync();

            await Task.WhenAll(countTask, docsTask);

            var docs = docsTask.Result;
            var total = countTask.Result;

            return new PagedResult<LoyaltyPointAudit>
            {
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                Items = docs
            };
        }
    }
}
