using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IReturnRepository
    {
        Task<ReturnOrder> InsertAsync(ReturnOrder r);
        Task<List<ReturnOrder>> GetByUserIdAsync(string userId);
        Task<ReturnOrder?> GetByIdAsync(string id);
        Task UpdateAsync(ReturnOrder r);
        Task<List<ReturnOrder>> GetByOrderNumberAsync(string? orderNumber);
        Task<List<ReturnOrder>> GetAllAsync();

    }
}
