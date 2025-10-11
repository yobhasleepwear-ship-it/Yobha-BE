using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface IWishlistRepository
    {
        // raw persisted models (backwards compatibility)
        Task<IEnumerable<Wishlist>> GetForUserAsync(string userId);
        Task<WishlistItemResponse> AddAsync(string userId, string productId, string? variantSku = null, int desiredQuantity = 1, string? desiredSize = null, string? desiredColor = null, bool notify = true, string? note = null);
        Task<bool> RemoveAsync(string userId, string productId);
        Task<bool> RemoveByIdAsync(string userId, string wishlistId);

        // DTO-based helpers for UI
        Task<IEnumerable<ShoppingPlatform.DTOs.WishlistItemResponse>> GetForUserDtoAsync(string userId);
    }
}
