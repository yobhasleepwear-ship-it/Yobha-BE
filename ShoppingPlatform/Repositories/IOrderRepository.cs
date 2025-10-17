using ShoppingPlatform.Dto;
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
        Task<bool> UpdateAsync(string id, Order order); // new
        Task<PagedResult<Order>> GetOrdersAdminAsync(
            int page,
            int pageSize,
            string sort,
            OrderFilter filter,
            CancellationToken ct);
        Task<bool> DeleteAsync(string id);

        Task<long> GetUserOrderCountAsync(string userId);


    }
}
