using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CartController : ControllerBase
    {
        private readonly ICartRepository _repo;

        public CartController(ICartRepository repo)
        {
            _repo = repo;
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<ApiResponse<CartResponse>>> Get()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var dto = await _repo.GetForUserDtoAsync(userId);
            return Ok(ApiResponse<CartResponse>.Ok(dto, "OK"));
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ApiResponse<CartItemResponse>>> AddOrUpdate([FromBody] AddOrUpdateCartRequest request)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var item = await _repo.AddOrUpdateAsync(userId, request.ProductId, request.VariantSku, request.Quantity, request.Currency, request.Note);
            return Ok(ApiResponse<CartItemResponse>.Ok(item, "Added/Updated"));
        }

        [HttpPatch("quantity")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<CartItemResponse>>> UpdateQuantity([FromBody] UpdateCartQuantityRequest request)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var item = await _repo.UpdateQuantityAsync(userId, request.CartItemId, request.Quantity);
            return Ok(ApiResponse<CartItemResponse>.Ok(item, "Updated"));
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Remove(string id)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.RemoveAsync(userId, id);
            return Ok(ApiResponse<object>.Ok(null, "Removed"));
        }

        [HttpDelete("clear")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Clear()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.ClearAsync(userId);
            return Ok(ApiResponse<object>.Ok(null, "Cleared cart"));
        }
    }
}