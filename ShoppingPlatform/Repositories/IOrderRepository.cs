using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface IOrderRepository
    {
        Task<IEnumerable<Order>> GetForUserAsync(string userId);
        Task<Order?> GetByIdAsync(string id);
        Task<Order> CreateAsync(Order order);
        Task<bool> UpdateStatusAsync(string id, string status);
    }
}
