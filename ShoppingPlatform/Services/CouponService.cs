using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using System;
using System.Threading.Tasks;
using static ShoppingPlatform.Services.ICouponService;

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
        Task<CouponValidationResult> ValidateOnlyAsync(string code, string userId, decimal orderAmount);
        Task<ClaimResult> TryClaimAndRecordAsync(string code, string userId, string? orderId = null, decimal? discountAmount = null);
        Task<bool> MarkUsedAsync(string couponId, string userId, string? orderId = null, decimal? discountAmount = null);

    }

    public class CouponValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public Coupon? Coupon { get; set; }
    }

    public class ClaimResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Coupon? Coupon { get; set; }
    }

    public class CouponService : ICouponService
    {
        private readonly ICouponRepository _repo;

        public CouponService(ICouponRepository repo)
        {
            _repo = repo;
        }

        /// <summary>
        /// Validate coupon without mutating DB. Returns discount amount and final amount.
        /// </summary>
        public async Task<CouponValidationResult> ValidateOnlyAsync(string code, string userId, decimal orderAmount)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon code required" };

            var coupon = await _repo.GetByCodeAsync(code.Trim().ToUpperInvariant());
            if (coupon == null || !coupon.IsActive)
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon not found or inactive" };

            var now = DateTime.UtcNow;
            if (coupon.StartAt.HasValue && coupon.StartAt.Value > now)
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon not active yet" };
            if (coupon.EndAt.HasValue && coupon.EndAt.Value < now)
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon expired" };

            if (coupon.MinOrderAmount.HasValue && orderAmount < coupon.MinOrderAmount.Value)
                return new CouponValidationResult { IsValid = false, ErrorMessage = $"Minimum order amount is {coupon.MinOrderAmount.Value}" };

            if (coupon.GlobalUsageLimit.HasValue && coupon.UsedCount >= coupon.GlobalUsageLimit.Value)
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon has reached maximum redemptions" };

            // per-user check (best-effort, read-only)
            var userUsed = await _repo.HasUserUsedAsync(coupon.Id!, userId);
            if (coupon.PerUserUsageLimit.HasValue && coupon.PerUserUsageLimit.Value <= 1 && userUsed)
                return new CouponValidationResult { IsValid = false, ErrorMessage = "Coupon already used by this user" };

            // compute discount
            decimal discount = 0m;
            if (coupon.Type == CouponType.Percentage)
            {
                discount = Math.Round(orderAmount * coupon.Value / 100m, 2, MidpointRounding.AwayFromZero);
                if (coupon.MaxDiscountAmount.HasValue) discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
            }
            else // Fixed
            {
                discount = coupon.Value;
            }

            discount = Math.Min(discount, orderAmount);
            discount = Math.Max(discount, 0m);
            decimal final = Math.Round(orderAmount - discount, 2);

            return new CouponValidationResult
            {
                IsValid = true,
                Coupon = coupon,
                DiscountAmount = discount,
                FinalAmount = final
            };
        }

        /// <summary>
        /// Attempts to claim coupon atomically and records an audit usage row.
        /// If claim fails, returns Failure.
        /// </summary>
        public async Task<ClaimResult> TryClaimAndRecordAsync(string code, string userId, string? orderId = null, decimal? discountAmount = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return new ClaimResult { Success = false, Error = "Coupon code required" };

            var claimed = await _repo.TryClaimCouponAsync(code.Trim().ToUpperInvariant(), userId);
            if (claimed == null)
                return new ClaimResult { Success = false, Error = "Coupon could not be claimed (limit reached or already used)" };

            // record audit usage (best-effort)
            try
            {
                var usage = new CouponUsage
                {
                    CouponId = claimed.Id!,
                    UserId = userId,
                    OrderId = orderId,
                    DiscountAmount = discountAmount ?? 0m,
                };

                await _repo.AddUsageAsync(usage);
            }
            catch
            {
                // don't fail the claim if audit write fails; just log in real app
            }

            return new ClaimResult { Success = true, Coupon = claimed };
        }

        public async Task<bool> MarkUsedAsync(string couponId, string userId, string? orderId = null, decimal? discountAmount = null)
        {
            if (string.IsNullOrWhiteSpace(couponId) || string.IsNullOrWhiteSpace(userId))
                return false;

            // Atomically mark coupon used by id
            var updatedCoupon = await _repo.MarkUsedByIdAsync(couponId, userId);
            if (updatedCoupon == null)
            {
                // claim failed (limit reached or user already used)
                return false;
            }

            // Record audit usage (best-effort; do not fail the main flow if audit insert fails)
            try
            {
                var usage = new CouponUsage
                {
                    CouponId = updatedCoupon.Id!,
                    UserId = userId,
                    OrderId = orderId,
                    DiscountAmount = discountAmount ?? 0m,
                };

                await _repo.AddUsageAsync(usage);
            }
            catch
            {
                // log in a real app; swallow so we don't make caller fail due to audit insertion issues
            }

            return true;
        }

    }
}
