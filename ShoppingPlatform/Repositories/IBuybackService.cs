using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IBuybackService
    {
        /// <summary>
        /// Creates a new buyback request.
        /// </summary>
        Task<BuybackRequest> CreateBuybackAsync(BuybackRequest request);

        /// <summary>
        /// Gets all buybacks for a specific user sorted by latest.
        /// </summary>
        Task<IEnumerable<BuybackRequest>> GetBuybacksByUserAsync(string userId);

        /// <summary>
        /// Mock schedule pickup integration (Delhivery).
        /// Updates delivery status to 'inTransit'.
        /// </summary>
        Task<BuybackRequest> SchedulePickupAsync(string buybackId);

        /// <summary>
        /// Fetches buyback details by orderId & productId or just productId.
        /// </summary>
        Task<IEnumerable<BuybackRequest>> GetBuybackDetailsAsync(string orderId, string productId);

        /// <summary>
        /// Admin update: buyback status, final status, loyalty points.
        /// </summary>
        Task<BuybackRequest> UpdateBuybackAsync(AdminUpdateBuybackRequest request);
    }
}
