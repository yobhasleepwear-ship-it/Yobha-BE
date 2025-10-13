using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface ICouponRepository
    {
        Task<Coupon?> GetByCodeAsync(string code);
        Task CreateAsync(Coupon coupon);
        Task<bool> IncrementUsageCountAsync(string couponId);
        Task<bool> HasUserUsedAsync(string couponId, string userId);
        Task AddUsageAsync(CouponUsage usage);
        Task<List<Coupon>> GetActiveCouponsAsync();
    }
}