using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<ActionResult<ApiResponse<IEnumerable<CartItem>>>> Get()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var items = await _repo.GetForUserAsync(userId);

            var resp = ApiResponse<IEnumerable<CartItem>>.Ok(items, "OK");
            return Ok(resp);
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> AddOrUpdate([FromBody] CartItem dto)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.AddOrUpdateAsync(userId, dto.ProductId, dto.VariantSku, dto.Quantity);

            var resp = ApiResponse<object>.Ok(null, "Added/Updated");
            return Ok(resp);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Remove(string id)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.RemoveAsync(userId, id);

            var resp = ApiResponse<object>.Ok(null, "Removed");
            return Ok(resp);
        }

        [HttpDelete("clear")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Clear()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.ClearAsync(userId);

            var resp = ApiResponse<object>.Ok(null, "Cleared cart");
            return Ok(resp);
        }
    }
}
