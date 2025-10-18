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
                return Unauthorized(ApiResponse<Coupon>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            // fetch user details to also include loyalty points
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                return NotFound(ApiResponse<Coupon>.Fail("User not found", null, HttpStatusCode.NotFound));

            var now = DateTime.UtcNow;

            // fetch active coupons
            var coupons = await _repo.GetActiveCouponsAsync();

            List<Coupon> result = new List<Coupon>();
            foreach (var c in coupons)
            {
                if (c.Code == "FIRST100")
                {
                    var isFirst100 = await _userRepo.checkFirst100User(userId);
                    if (isFirst100)
                    {
                        var isCouponUsed = await _repo.HasUserUsedAsync(c.Id, userId);
                        if(!isCouponUsed)
                            result.Add(c);
                    
                    }
                }
                else//(c.Code == "WELCOME")
                {

                        var isCouponUsed = await _repo.HasUserUsedAsync(c.Id, userId);
                        if (!isCouponUsed)
                            result.Add(c);
                }               

            }

            var response = new CouponRes
            {
                Coupons = result,
                LoyaltyPoints = user.LoyaltyPoints ?? 0m
            };

            return Ok(ApiResponse<CouponRes>.Ok(response, "OK"));
        }

        [HttpGet("all")]
        [Authorize] // keep it restricted on prod; change to AllowAnonymous if you need open access temporarily
        public async Task<IActionResult> GetAllTyped()
        {
            try
            {
                var coupons = await _repo.GetActiveCouponsAsync(); // existing method or use below repo code
                return Ok(ApiResponse<List<Coupon>>.Ok(coupons, "OK"));
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "GetAllTyped failed");
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    ApiResponse<string>.Fail($"Failed to load typed coupons: {ex.Message}", null, HttpStatusCode.InternalServerError));
            }
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
        public List<Coupon> Coupons { get; set; } = new List<Coupon>();
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
