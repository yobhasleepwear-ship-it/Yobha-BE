using ShoppingPlatform.Models;
using ShoppingPlatform.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface IProductRepository
    {
        // Query returns list of DTOs for listing and total count (for pagination)
        Task<(List<ProductListItemDto> items, long total)> QueryAsync(string? q, string? category,
            decimal? minPrice, decimal? maxPrice, int page, int pageSize, string? sort);

        Task<Product?> GetByIdAsync(string id);

        // New: lookup by readable PID (e.g., PID2138282)
        Task<Product?> GetByProductIdAsync(string productId);

        // New: check if readable PID exists
        Task<bool> ExistsByProductIdAsync(string productId);

        Task CreateAsync(Product product);
        Task UpdateAsync(Product product);
        Task DeleteAsync(string id);

        Task AddImageAsync(string id, ProductImage image);
        Task<bool> RemoveImageAsync(string id, string keyOrUrl);

        Task AddReviewAsync(string id, Review review);

        Task<List<CategoryCount>> GetCategoriesAsync();

        // Inventory helpers
        Task<bool> TryDecrementVariantQuantityAsync(string productId, string variantSku, int qty);
        Task<bool> IncrementVariantQuantityAsync(string productId, string variantSku, int qty);

        // Admin review moderation helpers
        Task<IEnumerable<Review>> GetPendingReviewsAsync(int page = 1, int pageSize = 50);
        Task<bool> ApproveReviewAsync(string productId, string reviewId);
        Task<bool> RejectReviewAsync(string productId, string reviewId);
    }
}
