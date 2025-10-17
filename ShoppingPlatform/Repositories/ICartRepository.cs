using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Repositories
{
    public interface ICartRepository
    {
        // keep the DTO-based APIs
        Task<CartResponse> GetForUserDtoAsync(string userId);
        Task<CartItemResponse> AddOrUpdateAsync(string userId, string productId, string? size, int quantity, string? currency , string? note = null);
        Task<CartItemResponse> UpdateQuantityAsync(string userId, string cartItemId, int quantity);
        Task RemoveAsync(string userId, string cartItemId);
        Task ClearAsync(string userId);

        // --- ADD THIS: return raw persisted CartItem models (used by Orders)
        Task<IEnumerable<CartItem>> GetForUserAsync(string userId);
    }
}