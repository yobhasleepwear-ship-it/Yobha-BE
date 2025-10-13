using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Services
{
    public class CouponResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }

        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public Coupon? Coupon { get; set; }
    }

    public interface ICouponService
    {
        // Validate and compute discount but DO NOT persist usage.
        Task<CouponResult> ValidateOnlyAsync(string code, string userId, decimal orderAmount);

        // After payment success: mark coupon used (persist usage and increment counts).
        Task<bool> MarkUsedAsync(string couponId, string userId, string orderId);
    }

    public class CouponService : ICouponService
    {
        private readonly ICouponRepository _repo;

        public CouponService(ICouponRepository repo)
        {
            _repo = repo;
        }

        public async Task<CouponResult> ValidateOnlyAsync(string code, string userId, decimal orderAmount)
        {
            var res = new CouponResult { IsValid = false };

            if (string.IsNullOrWhiteSpace(code))
            {
                res.ErrorMessage = "couponCode is required";
                return res;
            }

            var coupon = await _repo.GetByCodeAsync(code.Trim().ToUpperInvariant());
            if (coupon == null || !coupon.IsActive)
            {
                res.ErrorMessage = "Invalid coupon";
                return res;
            }

            var now = DateTime.UtcNow;
            if (coupon.StartAt.HasValue && now < coupon.StartAt.Value)
            {
                res.ErrorMessage = "Coupon not active yet";
                return res;
            }

            if (coupon.EndAt.HasValue && now > coupon.EndAt.Value)
            {
                res.ErrorMessage = "Coupon expired";
                return res;
            }

            if (coupon.GlobalUsageLimit.HasValue && coupon.UsedCount >= coupon.GlobalUsageLimit.Value)
            {
                res.ErrorMessage = "Coupon usage limit reached";
                return res;
            }

            if (coupon.MinOrderAmount.HasValue && orderAmount < coupon.MinOrderAmount.Value)
            {
                res.ErrorMessage = $"Order must be at least {coupon.MinOrderAmount.Value}";
                return res;
            }

            if (coupon.FirstOrderOnly)
            {
                var used = await _repo.HasUserUsedAsync(coupon.Id!, userId);
                if (used)
                {
                    res.ErrorMessage = "Coupon valid only on first order";
                    return res;
                }
            }

            decimal discount = 0M;
            if (coupon.Type == CouponType.Percentage)
            {
                discount = Math.Round(orderAmount * (coupon.Value / 100M), 2);
                if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
                    discount = coupon.MaxDiscountAmount.Value;
            }
            else
            {
                discount = coupon.Value;
            }

            if (discount > orderAmount) discount = orderAmount;
            var final = Math.Round(orderAmount - discount, 2);

            res.IsValid = true;
            res.DiscountAmount = discount;
            res.FinalAmount = final;
            res.Coupon = coupon;
            return res;
        }

        public async Task<bool> MarkUsedAsync(string couponId, string userId, string orderId)
        {
            if (string.IsNullOrWhiteSpace(couponId) || string.IsNullOrWhiteSpace(userId)) return false;

            // Optionally: ensure not already recorded for this order
            var already = await _repo.HasUserUsedAsync(couponId, userId);
            if (already)
            {
                // if already used by user (for FirstOrderOnly), return false to indicate not recorded again
                return false;
            }

            // increment coupon used count
            var inc = await _repo.IncrementUsageCountAsync(couponId);
            // add usage record
            var usage = new CouponUsage { CouponId = couponId, UserId = userId, OrderId = orderId, UsedAt = DateTime.UtcNow };
            await _repo.AddUsageAsync(usage);

            return inc;
        }
    }
}
