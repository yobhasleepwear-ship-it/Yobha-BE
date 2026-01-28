using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using System.Security.Claims;

namespace ShoppingPlatform.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BuybackController : ControllerBase
    {

        private readonly IBuybackService _buybackService;

        public BuybackController(IBuybackService buybackService)
        {
            _buybackService = buybackService;
        }

        // POST api/buyback/create
        [HttpPost("create")]
        [Authorize]
        public async Task<IActionResult> CreateBuyback([FromBody] CreateBuybackDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(ApiResponse<string>.Fail("User id not present in token."));

            // Validate request type
            if (string.IsNullOrWhiteSpace(dto.RequestType))
                return BadRequest(ApiResponse<string>.Fail("RequestType is required (TradeIn, RepairReuse, Recycle)."));

            var model = new BuybackRequest
            {
                Id = ObjectId.GenerateNewId().ToString(),
                UserId = userId,

                OrderId = dto.OrderId,
                ProductId = dto.ProductId,
                ProductUrl = dto.ProductUrl ?? new List<string>(),
                InvoiceUrl = dto.InvoiceUrl,
                Country = dto.Country,

                Quiz = dto.Quiz ?? new List<QuizItem>(),
                BuybackStatus = "pending",

                // New fields
                RequestType = dto.RequestType,     // TradeIn, RepairReuse, Recycle
                Amount = dto.Amount,               // Only for Repair & Reuse (admin finalizes)
                LoyaltyPoints = dto.LoyaltyPoints, // Only for TradeIn / Recycle (admin finalizes)
                Currency = dto.Currency ?? "INR",

                PaymentMethod = "razorpay",             // Default
                PaymentStatus = "Pending",

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _buybackService.CreateBuybackAsync(model);

            return Ok(ApiResponse<BuybackRequest>.Ok(created, "Buyback request submitted successfully."));
        }


        // GET api/buyback/getBuyBackDetails
        [HttpGet("getBuyBackDetails")]
        [Authorize]
        public async Task<IActionResult> GetBuyBackDetails()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized(ApiResponse<string>.Fail("User id not present in token."));

                var list = await _buybackService.GetBuybacksByUserAsync(userId);
                return Ok(ApiResponse<object>.Ok(list));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

        [HttpGet("admin/get")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetBuyback([FromQuery] string? orderId, [FromQuery] string? productId, [FromQuery] string? buybackId, [FromQuery] int page = 1, [FromQuery] int size = 20)
        {
            try
            {
                // normalize page/size
                page = Math.Max(1, page);
                size = Math.Clamp(size, 1, 100); // cap page size to 100

                var result = await _buybackService.GetBuybackDetailsAsync(orderId, productId, buybackId, page, size);
                return Ok(ApiResponse<PagedResult<BuybackRequest>>.Ok(result));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

        [HttpPut("admin/update")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateBuyback([FromBody] AdminUpdateBuybackRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.BuybackId))
                    return BadRequest(ApiResponse<string>.Fail("buybackId is required"));

                var updated = await _buybackService.AdminApproveOrUpdateBuybackAsync(request);
                return Ok(ApiResponse<BuybackRequest>.Ok(updated, "Buyback record updated."));
            }
            catch (KeyNotFoundException knf)
            {
                return NotFound(ApiResponse<string>.Fail(knf.Message));
            }
            catch (ArgumentException aex)
            {
                return BadRequest(ApiResponse<string>.Fail(aex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

        // POST api/buyback/pay/{buybackId}
        [HttpPost("pay/{buybackId}")]
        [Authorize]
        public async Task<IActionResult> CreateBuybackPayment(string buybackId)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized(ApiResponse<string>.Fail("User id not present in token."));

                var paymentResult = await _buybackService.InitiateBuybackPaymentAsync(buybackId, userId);
                return Ok(ApiResponse<object>.Ok(paymentResult, "Payment initiated."));
            }
            catch (KeyNotFoundException knf)
            {
                return NotFound(ApiResponse<string>.Fail(knf.Message));
            }
            catch (InvalidOperationException iop)
            {
                return BadRequest(ApiResponse<string>.Fail(iop.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

    }
}
