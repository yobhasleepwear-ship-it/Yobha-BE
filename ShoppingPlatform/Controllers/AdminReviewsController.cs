using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        public async Task<ActionResult<ApiResponse<object>>> GetPending([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 50;

            var pending = await _productRepo.GetPendingReviewsAsync(page, pageSize);

            var result = new
            {
                page,
                pageSize,
                items = pending
            };

            return Ok(ApiResponse<object>.Ok(result, "Pending reviews fetched"));
        }

        /// <summary>
        /// Approve a review.
        /// </summary>
        [HttpPost("{productId}/{reviewId}/approve")]
        public async Task<ActionResult<ApiResponse<object>>> Approve(string productId, string reviewId)
        {
            var ok = await _productRepo.ApproveReviewAsync(productId, reviewId);
            if (!ok)
            {
                var resp = ApiResponse<string>.Fail("Product or review not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            _logger.LogInformation("Admin {admin} approved review {reviewId} for product {productId}",
                User?.Identity?.Name ?? "unknown", reviewId, productId);

            var data = new { message = "Review approved", productId, reviewId };
            return Ok(ApiResponse<object>.Ok(data, "Review approved"));
        }

        /// <summary>
        /// Reject (delete) a review.
        /// </summary>
        [HttpPost("{productId}/{reviewId}/reject")]
        public async Task<ActionResult<ApiResponse<object>>> Reject(string productId, string reviewId)
        {
            var ok = await _productRepo.RejectReviewAsync(productId, reviewId);
            if (!ok)
            {
                var resp = ApiResponse<string>.Fail("Product or review not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            _logger.LogInformation("Admin {admin} rejected review {reviewId} for product {productId}",
                User?.Identity?.Name ?? "unknown", reviewId, productId);

            var data = new { message = "Review rejected", productId, reviewId };
            return Ok(ApiResponse<object>.Ok(data, "Review rejected"));
        }

        [HttpGet("product/{productId}")]
        public async Task<ActionResult<ApiResponse<object>>> GetReviewsForProduct(string productId, [FromQuery] bool? approved = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                var resp = ApiResponse<string>.Fail("productId is required", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            var product = await _productRepo.GetByIdAsync(productId);
            if (product == null)
            {
                var resp = ApiResponse<string>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            IEnumerable<Review> reviews = product.Reviews ?? Enumerable.Empty<Review>();

            if (approved.HasValue)
                reviews = reviews.Where(r => r.Approved == approved.Value);

            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 50;

            var total = reviews.Count();
            var items = reviews.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToList();

            var result = new
            {
                productId,
                productName = product.Name,
                page,
                pageSize,
                total,
                items
            };

            return Ok(ApiResponse<object>.Ok(result, "Product reviews fetched"));
        }

        [HttpPost("batch")]
        public async Task<ActionResult<ApiResponse<object>>> BatchModerate([FromBody] BatchModerationRequest req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
            {
                var resp = ApiResponse<string>.Fail("No items provided", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            var action = req.Action?.Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject")
            {
                var resp = ApiResponse<string>.Fail("Action must be 'approve' or 'reject'", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            var results = new List<object>();
            foreach (var item in req.Items)
            {
                bool ok = action == "approve"
                    ? await _productRepo.ApproveReviewAsync(item.ProductId, item.ReviewId)
                    : await _productRepo.RejectReviewAsync(item.ProductId, item.ReviewId);

                results.Add(new { item.ProductId, item.ReviewId, action, success = ok });
            }

            var summary = new { summary = results };
            return Ok(ApiResponse<object>.Ok(summary, "Batch moderation completed"));
        }
    }
}
