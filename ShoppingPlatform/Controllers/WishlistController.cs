using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Helpers;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : ControllerBase
    {
        private readonly IWishlistRepository _repo;

        public WishlistController(IWishlistRepository repo) => _repo = repo;

        // GET /api/wishlist
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<ApiResponse<IEnumerable<WishlistItemResponse>>>> Get()
        {
            var userId = User.GetUserIdOrAnonymous();
            var list = await _repo.GetForUserDtoAsync(userId);
            return Ok(ApiResponse<IEnumerable<WishlistItemResponse>>.Ok(list, "OK"));
        }

        // POST /api/wishlist
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ApiResponse<WishlistItemResponse>>> Add([FromBody] AddWishlistRequest request)
        {
            // Validate and convert ModelState -> List<string>
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message ?? "Invalid value" : e.ErrorMessage)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                return BadRequest(ApiResponse<WishlistItemResponse>.Fail("Invalid request", errors, System.Net.HttpStatusCode.BadRequest));
            }

            var userId = User.GetUserIdOrAnonymous();

            // Repo AddAsync returns Task (no DTO). It inserts or updates snapshot.
            await _repo.AddAsync(
                userId,
                request.ProductId,
                request.VariantSku,
                request.DesiredQuantity,
                request.DesiredSize,
                request.DesiredColor,
                request.NotifyWhenBackInStock,
                request.Note
            );

            // Try to fetch the newly-created/updated DTO from DB
            var all = await _repo.GetForUserDtoAsync(userId);
            var created = all.FirstOrDefault(i =>
                string.Equals(i.Product.ProductId, request.ProductId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Product.VariantSku ?? string.Empty, request.VariantSku ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            );

            if (created != null)
            {
                // return 201 Created with typed body (ApiResponse<T>)
                return CreatedAtAction(nameof(Get), new { }, ApiResponse<WishlistItemResponse>.Created(created, "Added to wishlist"));
            }

            // Fallback: if we could not read it back (very rare), return 200 OK typed envelope
            return Ok(ApiResponse<WishlistItemResponse>.Ok(null, "Added to wishlist"));
        }

        // DELETE /api/wishlist/product/{productId}
        [HttpDelete("product/{productId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> RemoveByProduct(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
                return BadRequest(ApiResponse<object>.Fail("productId is required", new List<string> { "productId is required" }, System.Net.HttpStatusCode.BadRequest));

            var userId = User.GetUserIdOrAnonymous();
            await _repo.RemoveAsync(userId, productId);
            return Ok(ApiResponse<object>.Ok(null, "Removed from wishlist"));
        }

        // DELETE /api/wishlist/{id}
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> RemoveById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(ApiResponse<object>.Fail("id is required", new List<string> { "id is required" }, System.Net.HttpStatusCode.BadRequest));

            var userId = User.GetUserIdOrAnonymous();
            await _repo.RemoveByIdAsync(userId, id);
            return Ok(ApiResponse<object>.Ok(null, "Removed from wishlist"));
        }
    }
}
