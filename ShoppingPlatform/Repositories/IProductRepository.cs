using System.Collections.Generic;
using System.Threading.Tasks;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IProductRepository
    {
        Task<Product?> GetByIdAsync(string id);
        Task<(IEnumerable<Product> Items, long Total)> QueryAsync(string? q, string? category, decimal? minPrice, decimal? maxPrice, int page, int pageSize, string? sort);
        Task CreateAsync(Product product);
        Task UpdateAsync(Product product);
        Task DeleteAsync(string id);
        Task AddImageAsync(string productId, ProductImage image);
        Task AddReviewAsync(string productId, Review review);

        // Moderation APIs
        Task<IEnumerable<PendingReview>> GetPendingReviewsAsync(int page = 1, int pageSize = 50);
        Task<bool> ApproveReviewAsync(string productId, string reviewId);
        Task<bool> RejectReviewAsync(string productId, string reviewId);
        Task<bool> RemoveImageAsync(string productId, string imageUrlOrKey);
        Task<IEnumerable<(string Category, long Count)>> GetCategoriesAsync();

    }
}
