using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Services
{
    public class BuybackService : IBuybackService
    {
        private readonly IMongoCollection<BuybackRequest> _buybackCollection;
        private readonly IMongoCollection<UserMinimal> _userCollection;

        // Minimal representation of the User document for loyalty updates
        private class UserMinimal
        {
            public string Id { get; set; }
            public int LoyaltyPoints { get; set; }
        }

        public BuybackService(IMongoDatabase database)
        {
            _buybackCollection = database.GetCollection<BuybackRequest>("Buyback");
            _userCollection = database.GetCollection<UserMinimal>("Users");
        }

        /// <summary>
        /// Create a new buyback request.
        /// </summary>
        public async Task<BuybackRequest> CreateBuybackAsync(BuybackRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProductId))
                throw new ArgumentException("ProductId is required.");

            request.CreatedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;

            await _buybackCollection.InsertOneAsync(request);
            return request;
        }

        /// <summary>
        /// Get all buybacks for the logged-in user sorted by creation time.
        /// </summary>
        public async Task<IEnumerable<BuybackRequest>> GetBuybacksByUserAsync(string userId)
        {
            var filter = Builders<BuybackRequest>.Filter.Eq(x => x.UserId, userId);
            var sort = Builders<BuybackRequest>.Sort.Descending(x => x.CreatedAt);

            return await _buybackCollection.Find(filter).Sort(sort).ToListAsync();
        }

        /// <summary>
        /// Mock Delhivery integration — marks the request as inTransit.
        /// </summary>
        public async Task<BuybackRequest> SchedulePickupAsync(string buybackId)
        {
            var filter = Builders<BuybackRequest>.Filter.Eq(b => b.Id, buybackId);

            var trackingId = $"DLV-MOCK-{ObjectId.GenerateNewId()}";
            var update = Builders<BuybackRequest>.Update
                .Set(b => b.PickupTrackingId, trackingId)
                .Set(b => b.DeliveryStatus, "inTransit")
                .Set(b => b.PickupScheduledAt, DateTime.UtcNow)
                .Set(b => b.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<BuybackRequest>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updated = await _buybackCollection.FindOneAndUpdateAsync(filter, update, options);
            if (updated == null)
                throw new KeyNotFoundException($"Buyback record with ID {buybackId} not found.");

            return updated;
        }

        /// <summary>
        /// Admin side — fetch by orderId & productId or only productId.
        /// </summary>
        public async Task<IEnumerable<BuybackRequest>> GetBuybackDetailsAsync(string orderId, string productId)
        {
            FilterDefinition<BuybackRequest> filter;

            if (!string.IsNullOrWhiteSpace(orderId))
            {
                filter = Builders<BuybackRequest>.Filter.And(
                    Builders<BuybackRequest>.Filter.Eq(b => b.OrderId, orderId),
                    Builders<BuybackRequest>.Filter.Eq(b => b.ProductId, productId)
                );
            }
            else
            {
                filter = Builders<BuybackRequest>.Filter.Eq(b => b.ProductId, productId);
            }

            return await _buybackCollection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// Admin update: buyback status, final status, loyalty points (if Accepted).
        /// </summary>
        public async Task<BuybackRequest> UpdateBuybackAsync(AdminUpdateBuybackRequest request)
        {
            var filter = Builders<BuybackRequest>.Filter.Eq(b => b.Id, request.BuybackId);

            var update = Builders<BuybackRequest>.Update
                .Set(b => b.BuybackStatus, request.BuybackStatus ?? "pending")
                .Set(b => b.FinalStatus, request.FinalStatus ?? "pending")
                .Set(b => b.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<BuybackRequest>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updated = await _buybackCollection.FindOneAndUpdateAsync(filter, update, options);
            if (updated == null)
                throw new KeyNotFoundException($"Buyback record with ID {request.BuybackId} not found.");

            // ✅ Add loyalty points if status is "Accepted"
            if (request.LoyaltyPoint > 0 &&
                !string.IsNullOrWhiteSpace(request.FinalStatus) &&
                request.FinalStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
            {
                var userFilter = Builders<UserMinimal>.Filter.Eq(u => u.Id, updated.UserId);
                var userUpdate = Builders<UserMinimal>.Update.Inc(u => u.LoyaltyPoints, request.LoyaltyPoint);

                await _userCollection.UpdateOneAsync(userFilter, userUpdate);
            }

            return updated;
        }
    }
}
