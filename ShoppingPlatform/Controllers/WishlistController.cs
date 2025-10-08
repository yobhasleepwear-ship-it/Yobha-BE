using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Models;

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

            return Ok(new ApiResponse<IEnumerable<Wishlist>> { Success = true, Message = "OK", Data = list });
        }

        [HttpPost("{productId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Add(string productId)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.AddAsync(userId, productId);
            return Ok(new ApiResponse<object> { Success = true, Message = "Added to wishlist" });
        }

        [HttpDelete("{productId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Remove(string productId)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            await _repo.RemoveAsync(userId, productId);
            return Ok(new ApiResponse<object> { Success = true, Message = "Removed from wishlist" });
        }
    }
}
