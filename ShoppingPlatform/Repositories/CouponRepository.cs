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

        public Task CreateAsync(Coupon coupon) =>
            _coupons.InsertOneAsync(coupon);

        public async Task<bool> IncrementUsageCountAsync(string couponId)
        {
            var res = await _coupons.UpdateOneAsync(
                Builders<Coupon>.Filter.Eq(c => c.Id, couponId),
                Builders<Coupon>.Update.Inc(c => c.UsedCount, 1)
            );
            return res.ModifiedCount > 0;
        }

        public async Task AddUsageAsync(CouponUsage usage) =>
            await _usages.InsertOneAsync(usage);

        public async Task<bool> HasUserUsedAsync(string couponId, string userId)
        {
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
    }
}