using MongoDB.Bson;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Services
{
    public interface ILoyaltyPointAuditService
    {
        Task<RecordResult> RecordAsync(LoyaltyPointAudit audit, CancellationToken cancellationToken = default);
        Task<RecordResult> RecordSimpleAsync(string userId,
                                             string operation,
                                             decimal points,
                                             string reason,
                                             string? relatedEntityId = null,
                                             string? email = null,
                                             string? phone = null,
                                             decimal? balanceAfter = null,
                                             BsonDocument? metadata = null,
                                             CancellationToken cancellationToken = default);
        Task<PagedResult<LoyaltyPointAudit>> GetForUserAsync(
        string userId,
        int page = 1,
        int pageSize = 20);
    }
}
