using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/admin/reviews")]
    [Authorize(Roles = "Admin")]
    public class AdminReviewsController : ControllerBase
    {
        private readonly IProductRepository _productRepo;
        private readonly ILogger<AdminReviewsController> _logger;

        public AdminReviewsController(IProductRepository productRepo, ILogger<AdminReviewsController> logger)
        {
            _productRepo = productRepo;
            _logger = logger;
        }

        /// <summary>
        /// List pending (unapproved) reviews.
        /// </summary>
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var pending = await _productRepo.GetPendingReviewsAsync(page, pageSize);
            return Ok(new { page, pageSize, items = pending });
        }

        /// <summary>
        /// Approve a review.
        /// </summary>
        [HttpPost("{productId}/{reviewId}/approve")]
        public async Task<IActionResult> Approve(string productId, string reviewId)
        {
            var ok = await _productRepo.ApproveReviewAsync(productId, reviewId);
            if (!ok) return NotFound(new { message = "Product or review not found" });

            _logger.LogInformation("Admin {admin} approved review {reviewId} for product {productId}", User?.Identity?.Name ?? "unknown", reviewId, productId);
            return Ok(new { message = "Review approved" });
        }

        /// <summary>
        /// Reject (delete) a review.
        /// </summary>
        [HttpPost("{productId}/{reviewId}/reject")]
        public async Task<IActionResult> Reject(string productId, string reviewId)
        {
            var ok = await _productRepo.RejectReviewAsync(productId, reviewId);
            if (!ok) return NotFound(new { message = "Product or review not found" });

            _logger.LogInformation("Admin {admin} rejected review {reviewId} for product {productId}", User?.Identity?.Name ?? "unknown", reviewId, productId);
            return Ok(new { message = "Review rejected" });
        }


        [HttpGet("product/{productId}")]
        public async Task<IActionResult> GetReviewsForProduct(string productId, [FromQuery] bool? approved = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(productId)) return BadRequest();

            var product = await _productRepo.GetByIdAsync(productId);
            if (product == null) return NotFound();

            IEnumerable<Review> reviews = product.Reviews ?? Enumerable.Empty<Review>();

            if (approved.HasValue)
                reviews = reviews.Where(r => r.Approved == approved.Value);

            var total = reviews.Count();
            var items = reviews.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new { productId, productName = product.Name, page, pageSize, total, items });
        }

        [HttpPost("batch")]
        public async Task<IActionResult> BatchModerate([FromBody] BatchModerationRequest req)
        {
            if (req == null || req.Items == null || !req.Items.Any()) return BadRequest("No items provided");
            var action = req.Action?.Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return BadRequest("Action must be 'approve' or 'reject'");

            var results = new List<object>();
            foreach (var item in req.Items)
            {
                bool ok = action == "approve"
                    ? await _productRepo.ApproveReviewAsync(item.ProductId, item.ReviewId)
                    : await _productRepo.RejectReviewAsync(item.ProductId, item.ReviewId);

                results.Add(new { item.ProductId, item.ReviewId, action, success = ok });
            }

            return Ok(new { summary = results });
        }
    }
}
