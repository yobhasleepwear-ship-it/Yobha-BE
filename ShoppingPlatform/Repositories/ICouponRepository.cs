using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface ICouponRepository
    {
        Task<Coupon?> GetByCodeAsync(string code);
        Task<Coupon?> GetByIdAsync(string couponId);
        Task CreateAsync(Coupon coupon);

        /// <summary>
        /// Atomically increments usage and records the user (AddToSet semantics).
        /// Returns the updated Coupon document on success, or null on failure (limit reached / user already used).
        /// </summary>
        Task<Coupon?> TryClaimCouponAsync(string couponCode, string userId);

        /// <summary>
        /// Undo a previous claim (decrement UsedCount and remove userId from UsedByUserIds).
        /// </summary>
        Task UndoClaimAsync(string couponId, string userId);

        /// <summary>
        /// Add a usage row to couponUsages collection (optional audit).
        /// </summary>
        Task AddUsageAsync(CouponUsage usage);

        Task<bool> HasUserUsedAsync(string couponId, string userId);
        Task<List<Coupon>> GetActiveCouponsAsync();
        Task<Coupon?> MarkUsedByIdAsync(string couponId, string userId);

    }
}
