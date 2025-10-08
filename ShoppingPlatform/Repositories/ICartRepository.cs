using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface ICartRepository
    {
        Task<IEnumerable<CartItem>> GetForUserAsync(string userId);
        Task AddOrUpdateAsync(string userId, string productId, string variantSku, int quantity);
        Task RemoveAsync(string userId, string cartItemId);
        Task ClearAsync(string userId);
    }
}
