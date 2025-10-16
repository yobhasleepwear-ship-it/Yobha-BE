using MongoDB.Driver;
using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class CouponRepository : ICouponRepository
    {
        private readonly IMongoCollection<Coupon> _coupons;
        private readonly IMongoCollection<CouponUsage> _usages;

        public CouponRepository(IMongoDatabase db)
        {
            _coupons = db.GetCollection<Coupon>("coupons");
            _usages = db.GetCollection<CouponUsage>("couponUsages");

            // Indexes (helpful)
            var idx = Builders<Coupon>.IndexKeys.Ascending(c => c.Code);
            _coupons.Indexes.CreateOne(new CreateIndexModel<Coupon>(idx, new CreateIndexOptions { Unique = true }));

            var usageIdx = Builders<CouponUsage>.IndexKeys
                .Ascending(u => u.CouponId)
                .Ascending(u => u.UserId);
            _usages.Indexes.CreateOne(new CreateIndexModel<CouponUsage>(usageIdx));
        }

        public async Task<Coupon?> GetByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var normalized = code.Trim().ToUpperInvariant();
            return await _coupons.Find(c => c.Code == normalized).FirstOrDefaultAsync();
        }

        public async Task<Coupon?> GetByIdAsync(string couponId)
        {
            if (string.IsNullOrWhiteSpace(couponId)) return null;
            return await _coupons.Find(c => c.Id == couponId).FirstOrDefaultAsync();
        }

        public Task CreateAsync(Coupon coupon) =>
            _coupons.InsertOneAsync(coupon);

        /// <summary>
        /// Atomically claim a coupon for a user:
        /// - ensures UsedCount < GlobalUsageLimit (if set)
        /// - ensures user is not already in UsedByUserIds (for PerUserUsageLimit == 1)
        /// The update increments UsedCount and AddToSet the userId.
        /// Returns the updated coupon or null if claim failed.
        /// </summary>
        public async Task<Coupon?> TryClaimCouponAsync(string couponCode, string userId)
        {
            if (string.IsNullOrWhiteSpace(couponCode) || string.IsNullOrWhiteSpace(userId))
                return null;

            var normalized = couponCode.Trim().ToUpperInvariant();

            // fetch coupon once to determine limits (light read)
            var coupon = await GetByCodeAsync(normalized);
            if (coupon == null || !coupon.IsActive) return null;

            var filter = Builders<Coupon>.Filter.Eq(c => c.Id, coupon.Id);

            // ensure global usage limit still available if set
            if (coupon.GlobalUsageLimit.HasValue)
            {
                filter = filter & Builders<Coupon>.Filter.Lt(c => c.UsedCount, coupon.GlobalUsageLimit.Value);
            }

            // ensure user hasn't used it already when per-user limit == 1
            if (coupon.PerUserUsageLimit.HasValue && coupon.PerUserUsageLimit.Value == 1)
            {
                // Ensure the UsedByUserIds array does NOT contain this userId
                filter = filter & Builders<Coupon>.Filter.Not(Builders<Coupon>.Filter.AnyEq(c => c.UsedByUserIds, userId));
            }

            // Note: If PerUserUsageLimit > 1, repository currently does not enforce counts per-user;
            // that requires a per-user counter map or separate collection. For welcome/per-user-one this is fine.

            var update = Builders<Coupon>.Update
                .Inc(c => c.UsedCount, 1)
                .AddToSet(c => c.UsedByUserIds, userId);

            var options = new FindOneAndUpdateOptions<Coupon>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updated = await _coupons.FindOneAndUpdateAsync(filter, update, options);
            return updated; // null if filter didn't match (limit reached / user already used)
        }

        public async Task UndoClaimAsync(string couponId, string userId)
        {
            if (string.IsNullOrWhiteSpace(couponId) || string.IsNullOrWhiteSpace(userId)) return;

            var update = Builders<Coupon>.Update
                .Inc(c => c.UsedCount, -1)
                .Pull(c => c.UsedByUserIds, userId);

            await _coupons.UpdateOneAsync(c => c.Id == couponId, update);
        }

        public async Task AddUsageAsync(CouponUsage usage) =>
            await _usages.InsertOneAsync(usage);

        public async Task<bool> HasUserUsedAsync(string couponId, string userId)
        {
            if (string.IsNullOrWhiteSpace(couponId) || string.IsNullOrWhiteSpace(userId)) return false;

            // Check both coupon document's UsedByUserIds and the usages audit collection for redundancy
            var coupon = await GetByIdAsync(couponId);
            if (coupon != null && coupon.UsedByUserIds != null && coupon.UsedByUserIds.Contains(userId))
                return true;

            var filter = Builders<CouponUsage>.Filter.And(
                Builders<CouponUsage>.Filter.Eq(u => u.CouponId, couponId),
                Builders<CouponUsage>.Filter.Eq(u => u.UserId, userId)
            );
            var found = await _usages.Find(filter).Limit(1).FirstOrDefaultAsync();
            return found != null;
        }

        public async Task<List<Coupon>> GetActiveCouponsAsync()
        {
            var now = DateTime.UtcNow;
            var filter = Builders<Coupon>.Filter.And(
                Builders<Coupon>.Filter.Eq(c => c.IsActive, true),
                Builders<Coupon>.Filter.Or(
                  Builders<Coupon>.Filter.Eq(c => c.StartAt, null),
                  Builders<Coupon>.Filter.Lte(c => c.StartAt, now)
                ),
                Builders<Coupon>.Filter.Or(
                  Builders<Coupon>.Filter.Eq(c => c.EndAt, null),
                  Builders<Coupon>.Filter.Gte(c => c.EndAt, now)
                )
            );
            return await _coupons.Find(filter).ToListAsync();
        }

        public async Task<Coupon?> MarkUsedByIdAsync(string couponId, string userId)
        {
            if (string.IsNullOrWhiteSpace(couponId) || string.IsNullOrWhiteSpace(userId))
                return null;

            // Fetch coupon to inspect limits (light read)
            var coupon = await GetByIdAsync(couponId);
            if (coupon == null || !coupon.IsActive) return null;

            // Build filter ensuring global limit (if any) not exceeded and user hasn't used it already (for per-user=1)
            var filter = Builders<Coupon>.Filter.Eq(c => c.Id, coupon.Id);

            if (coupon.GlobalUsageLimit.HasValue)
            {
                filter = filter & Builders<Coupon>.Filter.Lt(c => c.UsedCount, coupon.GlobalUsageLimit.Value);
            }

            if (coupon.PerUserUsageLimit.HasValue && coupon.PerUserUsageLimit.Value == 1)
            {
                filter = filter & Builders<Coupon>.Filter.Not(Builders<Coupon>.Filter.AnyEq(c => c.UsedByUserIds, userId));
            }

            var update = Builders<Coupon>.Update
                .Inc(c => c.UsedCount, 1)
                .AddToSet(c => c.UsedByUserIds, userId);

            var options = new FindOneAndUpdateOptions<Coupon>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updated = await _coupons.FindOneAndUpdateAsync(filter, update, options);
            return updated; // null if not matched (limit reached or already used)
        }

    }
}
