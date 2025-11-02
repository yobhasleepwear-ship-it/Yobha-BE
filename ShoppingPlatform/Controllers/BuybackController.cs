using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
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
                FinalStatus = "pending",
                DeliveryStatus = "pending",
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

        // POST api/buyback/schedulePickup/{buybackId}
        [HttpPost("schedulePickup/{buybackId}")]
        [Authorize]
        public async Task<IActionResult> SchedulePickup(string buybackId)
        {
            try
            {
                // In a real flow you may also verify user owns the buyback; left generic here.
                var updated = await _buybackService.SchedulePickupAsync(buybackId);
                return Ok(ApiResponse<BuybackRequest>.Ok(updated, "Pickup scheduled (mock)."));
            }
            catch (KeyNotFoundException kex)
            {
                return NotFound(ApiResponse<string>.Fail(kex.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

        // ADMIN: GET api/buyback/admin/get?orderId=..&productId=..
        [HttpGet("admin/get")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetBuyback([FromQuery] string orderId, [FromQuery] string productId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productId))
                    return BadRequest(ApiResponse<string>.Fail("productId is required"));

                var list = await _buybackService.GetBuybackDetailsAsync(orderId, productId);
                return Ok(ApiResponse<object>.Ok(list));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

        // ADMIN: PUT api/buyback/admin/update
        [HttpPut("admin/update")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateBuyback([FromBody] AdminUpdateBuybackRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.BuybackId))
                    return BadRequest(ApiResponse<string>.Fail("buybackId is required"));

                var updated = await _buybackService.UpdateBuybackAsync(request);
                return Ok(ApiResponse<BuybackRequest>.Ok(updated, "Buyback record updated."));
            }
            catch (KeyNotFoundException knf)
            {
                return NotFound(ApiResponse<string>.Fail(knf.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<string>.Fail($"Server error: {ex.Message}"));
            }
        }

    }
}
