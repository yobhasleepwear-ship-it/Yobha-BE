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
    public class WishlistController : ControllerBase
    {
        private readonly IWishlistRepository _repo;

        public WishlistController(IWishlistRepository repo)
        {
            _repo = repo;
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<ApiResponse<IEnumerable<Wishlist>>>> Get()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            var list = await _repo.GetForUserAsync(userId);

            var response = ApiResponse<IEnumerable<Wishlist>>.Ok(list, "OK");
            return Ok(response);
        }

        [HttpPost("{productId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Add(string productId)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.AddAsync(userId, productId);

            var response = ApiResponse<object>.Ok(null, "Added to wishlist");
            return Ok(response);
        }

        [HttpDelete("{productId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Remove(string productId)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.RemoveAsync(userId, productId);

            var response = ApiResponse<object>.Ok(null, "Removed from wishlist");
            return Ok(response);
        }
    }
}
