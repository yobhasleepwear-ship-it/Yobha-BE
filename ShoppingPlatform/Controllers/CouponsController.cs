using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CouponsController : ControllerBase
    {
        private readonly ICouponRepository _repo;
        private readonly ICouponService _service;

        public CouponsController(ICouponRepository repo, ICouponService service)
        {
            _repo = repo;
            _service = service;
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

            var result = await _service.ValidateAndApplyAsync(code, userId, orderAmount, req.OrderId);

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
    }

    public class ApplyCouponRequest
    {
        public string CouponCode { get; set; } = null!;
        public decimal OrderAmount { get; set; }
        public string? OrderId { get; set; } // optional - if you want to tie usage to order
    }
}
