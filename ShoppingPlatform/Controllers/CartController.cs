using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Models;

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
        public async Task<ActionResult<ApiResponse<IEnumerable<CartItem>>>> Get()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var items = await _repo.GetForUserAsync(userId);
            return Ok(new ApiResponse<IEnumerable<CartItem>> { Success = true, Message = "OK", Data = items });
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> AddOrUpdate([FromBody] CartItem dto)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.AddOrUpdateAsync(userId, dto.ProductId, dto.VariantSku, dto.Quantity);
            return Ok(new ApiResponse<object> { Success = true, Message = "Added/Updated" });
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Remove(string id)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.RemoveAsync(userId, id);
            return Ok(new ApiResponse<object> { Success = true, Message = "Removed" });
        }

        [HttpDelete("clear")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Clear()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.ClearAsync(userId);
            return Ok(new ApiResponse<object> { Success = true, Message = "Cleared cart" });
        }
    }
}
