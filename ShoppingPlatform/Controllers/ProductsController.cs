using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Helpers;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductRepository _repo;
        private readonly IS3Service _s3;
        private readonly AwsS3Settings _aws;

        public ProductsController(IProductRepository repo, IS3Service s3, IOptions<AwsS3Settings> awsOptions)
        {
            _repo = repo;
            _s3 = s3;
            _aws = awsOptions.Value;
        }

        // -------------------------------------------
        // GET: api/products
        // Returns: ApiResponse<PagedResult<ProductListItemDto>>
        // -------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PagedResult<ProductListItemDto>>>> Query(
            [FromQuery] string? q,
            [FromQuery] string? category,
            [FromQuery] decimal? minPrice = null,
            [FromQuery] decimal? maxPrice = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = "latest",
            [FromQuery] string? country = null // optional filter (controller-level only)
        )
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            // Call the repository using the original signature you had earlier.
            // Expectation: QueryAsync returns a tuple (List<ProductListItemDto> items, long total)
            // If your repo returns different types, tell me exact signature and I'll update this.
            var (items, total) = await _repo.QueryAsync(q, category, minPrice, maxPrice, page, pageSize, sort);

            // If your repository returns domain Product objects instead of DTOs, switch to:
            // var (products, total) = await _repo.QueryAsync(...);
            // var items = ProductMappings.ToListItemDtos(products, country);
            // but since your repo originally returned DTOs, we map directly.

            var paged = new PagedResult<ProductListItemDto>
            {
                PageNumber = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            };

            var response = ApiResponse<PagedResult<ProductListItemDto>>.Ok(paged, "OK");
            return Ok(response);
        }

        // -------------------------------------------
        // GET: api/products/{id}
        // Returns ApiResponse<ProductDetailDto>
        // -------------------------------------------
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<ProductDetailDto>>> Get(string id)
        {
            var product = await _repo.GetByIdAsync(id);
            if (product == null)
            {
                var notFound = ApiResponse<ProductDetailDto>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            var dto = ProductMappings.ToDetailDto(product);

            var ok = ApiResponse<ProductDetailDto>.Ok(dto, "OK");
            return Ok(ok);
        }

        // -------------------------------------------
        // POST: api/products
        // Admin only - create new product
        // -------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<Product>>> Create([FromBody] Product product)
        {
            // Validate mandatory productMainCategory
            if (string.IsNullOrWhiteSpace(product.ProductMainCategory))
            {
                var bad = ApiResponse<Product>.Fail("productMainCategory is required", null, HttpStatusCode.BadRequest);
                return BadRequest(bad);
            }

            // Ensure a fresh id (Mongo will generate one if null)
            product.Id = null;
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;

            // Ensure price list item ids exist
            if (product.PriceList != null)
            {
                foreach (var p in product.PriceList)
                {
                    if (string.IsNullOrWhiteSpace(p.Id))
                        p.Id = Guid.NewGuid().ToString("N");
                }
            }

            await _repo.CreateAsync(product);

            var resp = ApiResponse<Product>.Created(product, "Product created");
            return CreatedAtAction(nameof(Get), new { id = product.Id }, resp);
        }

        // -------------------------------------------
        // PUT: api/products/{id}
        // Admin only - update product details or variants
        // -------------------------------------------
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<object>>> Update(string id, [FromBody] Product updated)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
            {
                var notFound = ApiResponse<object>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            if (string.IsNullOrWhiteSpace(updated.ProductMainCategory))
            {
                var bad = ApiResponse<object>.Fail("productMainCategory is required", null, HttpStatusCode.BadRequest);
                return BadRequest(bad);
            }

            updated.Id = id;
            updated.UpdatedAt = DateTime.UtcNow;

            if (updated.PriceList != null)
            {
                foreach (var p in updated.PriceList)
                {
                    if (string.IsNullOrWhiteSpace(p.Id))
                        p.Id = Guid.NewGuid().ToString("N");
                }
            }

            await _repo.UpdateAsync(updated);

            var ok = ApiResponse<object>.Ok(null, "Product updated");
            return Ok(ok);
        }

        // -------------------------------------------
        // DELETE: api/products/{id}
        // Admin only - soft delete
        // -------------------------------------------
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<object>>> Delete(string id)
        {
            await _repo.DeleteAsync(id);

            var ok = ApiResponse<object>.Ok(null, "Product deleted");
            return Ok(ok);
        }

        // -------------------------------------------
        // GET: api/products/{id}/images/presign
        // -------------------------------------------
        [HttpGet("{id}/images/presign")]
        [Authorize(Roles = "Admin")]
        public ActionResult<ApiResponse<object>> PresignImage(string id, [FromQuery] string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                var bad = ApiResponse<object>.Fail("Filename required", null, HttpStatusCode.BadRequest);
                return BadRequest(bad);
            }

            var key = $"products/{id}/{Guid.NewGuid()}_{Path.GetFileName(filename)}";
            var uploadUrl = _s3.GetPreSignedUploadUrl(_aws.Bucket, key, TimeSpan.FromMinutes(10));
            var objectUrl = _s3.GetObjectUrl(_aws.Bucket, key);

            var resp = ApiResponse<object>.Ok(new { uploadUrl, objectUrl, key }, "Presigned URL created");
            return Ok(resp);
        }

        // -------------------------------------------
        // POST: api/products/{id}/images
        // -------------------------------------------
        [HttpPost("{id}/images")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<ProductImage>>> AttachImage(string id, [FromBody] ProductImage image)
        {
            var product = await _repo.GetByIdAsync(id);
            if (product == null)
            {
                var notFound = ApiResponse<ProductImage>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            image.UploadedAt = DateTime.UtcNow;
            await _repo.AddImageAsync(id, image);

            var ok = ApiResponse<ProductImage>.Ok(image, "Image attached");
            return Ok(ok);
        }

        // -------------------------------------------
        // POST: api/products/{id}/reviews}
        // -------------------------------------------
        [HttpPost("{id}/reviews")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> AddReview(string id, [FromBody] Review review)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            review.UserId = userId;
            review.CreatedAt = DateTime.UtcNow;
            review.Approved = false;

            await _repo.AddReviewAsync(id, review);

            var accepted = new ApiResponse<object>
            {
                Success = true,
                Status = HttpStatusCode.Accepted,
                Message = "Review submitted for moderation",
                Data = null
            };

            return Accepted(accepted);
        }

        // -------------------------------------------
        // DELETE: api/products/{id}/images?keyOrUrl=...
        // -------------------------------------------
        [HttpDelete("{id}/images")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<object>>> DeleteImage(string id, [FromQuery] string keyOrUrl)
        {
            if (string.IsNullOrWhiteSpace(keyOrUrl))
            {
                var bad = ApiResponse<object>.Fail("keyOrUrl required", null, HttpStatusCode.BadRequest);
                return BadRequest(bad);
            }

            var bucket = string.IsNullOrEmpty(_aws.Bucket) ? _aws.BucketName : _aws.Bucket;
            var deletedFromS3 = await _s3.DeleteObjectAsync(bucket, keyOrUrl);
            var removed = await _repo.RemoveImageAsync(id, keyOrUrl);
            if (!removed)
            {
                var notFound = ApiResponse<object>.Fail("Image not found in product", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            var ok = ApiResponse<object>.Ok(new { s3Deleted = deletedFromS3 }, "Image removed");
            return Ok(ok);
        }

        // -------------------------------------------
        // GET: api/products/categories
        // -------------------------------------------
        [HttpGet("categories")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<CategoryCount>>>> GetCategories()
        {
            var cats = await _repo.GetCategoriesAsync();
            var ok = ApiResponse<List<CategoryCount>>.Ok(cats, "OK");
            return Ok(ok);
        }
    }
}
