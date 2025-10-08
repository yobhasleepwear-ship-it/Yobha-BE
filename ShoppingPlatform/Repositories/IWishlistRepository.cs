using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface IWishlistRepository
    {
        Task<IEnumerable<Wishlist>> GetForUserAsync(string userId);
        Task AddAsync(string userId, string productId);
        Task RemoveAsync(string userId, string productId);
    }
}
