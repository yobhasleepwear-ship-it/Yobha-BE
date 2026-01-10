using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
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
        /// Fetches buyback details by orderId & productId or just productId.
        /// </summary>
        Task<PagedResult<BuybackRequest>> GetBuybackDetailsAsync(string? orderId, string? productId, string? buybackId, int page = 1, int size = 20);

        /// <summary>
        /// Admin update: buyback status, final status, loyalty points.
        /// </summary>
        Task<BuybackRequest> AdminApproveOrUpdateBuybackAsync(AdminUpdateBuybackRequest request);
        Task<object> InitiateBuybackPaymentAsync(string buybackId, string userId);
        Task<bool> UpdateDeliveryDetailsAsync(
    string referenceId,
    DeliveryDetails request);
        Task<bool> UpdateDeliveryStatusAsync(string referenceId, string newStatus);
        Task<BuybackRequest?> GetByAwbAsync(string awb);
    }
}
