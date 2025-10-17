using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using ShoppingPlatform.Helpers;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using System;
using System.Net;
using System.Threading.Tasks;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponsController : ControllerBase
    {
        private readonly ICouponRepository _repo;
        private readonly ICouponService _service;
        private readonly IOrderRepository _orderRepo;
        private readonly UserRepository _userRepo;
        public CouponsController(ICouponRepository repo, ICouponService service,IOrderRepository orderRepo, UserRepository userRepo)
        {
            _repo = repo;
            _service = service;
            _orderRepo = orderRepo;
            _userRepo = userRepo;
        }

        // Admin: create coupon
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<Coupon>>> Create([FromBody] Coupon dto)
        {
            if (dto == null) return BadRequest(ApiResponse<Coupon>.Fail("Coupon payload required", null, System.Net.HttpStatusCode.BadRequest));
            dto.Code = dto.Code.Trim().ToUpperInvariant();
            dto.CreatedAt = DateTime.UtcNow;

            await _repo.CreateAsync(dto);
            return Created("", ApiResponse<Coupon>.Created(dto, "Coupon created"));
        }

        // Public: list active coupons (optionally authenticated)
        [HttpGet("active")]
        public async Task<ActionResult<ApiResponse<System.Collections.Generic.List<Coupon>>>> GetActive()
        {
            var list = await _repo.GetActiveCouponsAsync();
            return Ok(ApiResponse<System.Collections.Generic.List<Coupon>>.Ok(list, "OK"));
        }

        // User: validate & apply (returns discount info)
        [HttpPost("apply")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Apply([FromBody] ApplyCouponRequest req)
        {
            var userId = User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value;
            if (userId == null) return Unauthorized(ApiResponse<object>.Fail("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));

            var orderAmount = req.OrderAmount;
            var code = req.CouponCode;

            var result = await _service.ValidateOnlyAsync(code, userId, orderAmount);

            if (!result.IsValid)
                return BadRequest(ApiResponse<object>.Fail(result.ErrorMessage, null, System.Net.HttpStatusCode.BadRequest));

            var data = new
            {
                coupon = new { result.Coupon!.Code, result.Coupon.Type, result.Coupon.Value },
                discount = result.DiscountAmount,
                finalAmount = result.FinalAmount
            };

            return Ok(ApiResponse<object>.Ok(data, "Coupon applied"));
        }

        // GET /api/coupons/active-for-me?orderAmount=1200
        [HttpGet("active-for-me")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Coupon>>>> GetActiveForMe([FromQuery] decimal? orderAmount = null)
        {
            var userId = User.GetUserIdOrAnonymous();

            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<CouponRes>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            // fetch user details to also include loyalty points
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                return NotFound(ApiResponse<CouponRes>.Fail("User not found", null, HttpStatusCode.NotFound));

            var now = DateTime.UtcNow;

            // fetch active coupons
            var coupons = await _repo.GetActiveCouponsAsync();
            return Ok(ApiResponse<List<Coupon>>.Ok(coupons, "OK"));
            var result = new List<CouponSummaryDto>();
            var orderAmt = orderAmount ?? 0m;

            foreach (var c in coupons)
            {
                var dto = new CouponSummaryDto
                {
                    Id = c.Id ?? string.Empty,
                    Code = c.Code,
                    Description = c.Description ?? string.Empty,
                    MinOrderAmount = c.MinOrderAmount,
                    FirstOrderOnly = c.FirstOrderOnly,
                    AllowOncePerUser = (c.PerUserUsageLimit ?? 1) <= 1,
                    First100UsersOnly = c.GlobalUsageLimit.HasValue && c.GlobalUsageLimit.Value == 100
                };

                //// --- Validation sequence ---
                //if (!c.IsActive)
                //{
                //    dto.IsValid = false; dto.InvalidReason = "Coupon inactive";
                //    result.Add(dto); continue;
                //}

                //if (c.StartAt.HasValue && c.StartAt.Value > now)
                //{
                //    dto.IsValid = false; dto.InvalidReason = "Coupon not active yet";
                //    result.Add(dto); continue;
                //}

                //if (c.EndAt.HasValue && c.EndAt.Value < now)
                //{
                //    dto.IsValid = false; dto.InvalidReason = "Coupon expired";
                //    result.Add(dto); continue;
                //}

                //if (c.GlobalUsageLimit.HasValue && c.UsedCount >= c.GlobalUsageLimit.Value)
                //{
                //    dto.IsValid = false; dto.InvalidReason = "Coupon fully redeemed";
                //    result.Add(dto); continue;
                //}

                // Per-user usage check
                var userUsed = await _repo.HasUserUsedAsync(c.Id!, userId);
                if (userUsed)
                {
                    dto.IsValid = false; dto.InvalidReason = "Already used by this user";
                    result.Add(dto); continue;
                }

                // First order only
                //if (c.FirstOrderOnly)
                //{
                //    var priorOrders = await _orderRepo.GetUserOrderCountAsync(userId);
                //    if (priorOrders > 0)
                //    {
                //        dto.IsValid = false; dto.InvalidReason = "Coupon valid only on first order";
                //        result.Add(dto); continue;
                //    }
                //}

                //// First 100 users only (explicit limit)
                //if (c.GlobalUsageLimit.HasValue && c.GlobalUsageLimit.Value == 100 && c.UsedCount >= c.GlobalUsageLimit)
                //{
                //    dto.IsValid = false; dto.InvalidReason = "Coupon limited to first 100 users and fully claimed";
                //    result.Add(dto); continue;
                //}

                //// Min order amount
                //if (c.MinOrderAmount.HasValue && orderAmt > 0 && orderAmt < c.MinOrderAmount.Value)
                //{
                //    dto.IsValid = false; dto.InvalidReason = $"Minimum order value ₹{c.MinOrderAmount.Value} required";
                //    result.Add(dto); continue;
                //}

                // Estimate discount
                if (orderAmt > 0)
                {
                    decimal estimated = 0m;
                    if (c.Type == CouponType.Percentage)
                    {
                        estimated = Math.Round(orderAmt * c.Value / 100m, 2);
                        if (c.MaxDiscountAmount.HasValue)
                            estimated = Math.Min(estimated, c.MaxDiscountAmount.Value);
                    }
                    else
                    {
                        estimated = c.Value;
                    }

                    estimated = Math.Min(estimated, orderAmt);
                    dto.EstimatedDiscountAmount = estimated;
                }

                dto.IsValid = true;
                result.Add(dto);
            }

            var response = new CouponRes
            {
                CouponSummaryDtos = result,
                LoyaltyPoints = user.LoyaltyPoints ?? 0m
            };

            return Ok(ApiResponse<CouponRes>.Ok(response, "OK"));
        }


    }

    public class ApplyCouponRequest
    {
        public string CouponCode { get; set; } = null!;
        public decimal OrderAmount { get; set; }
        public string? OrderId { get; set; } // optional - if you want to tie usage to order
    }

    public class CouponRes
    {
        public List<CouponSummaryDto> CouponSummaryDtos { get; set; } = new List<CouponSummaryDto>();
        public decimal? LoyaltyPoints { get; set; }
    }


    public class CouponSummaryDto
    {
        public string Id { get; set; } = null!;
        public string Code { get; set; } = null!;
        public string Description { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string? InvalidReason { get; set; }
        public decimal EstimatedDiscountAmount { get; set; } = 0m; // optional: if orderAmount provided
        public decimal? MinOrderAmount { get; set; }
        public bool FirstOrderOnly { get; set; }
        public bool AllowOncePerUser { get; set; }
        public bool First100UsersOnly { get; set; }
    }

}
