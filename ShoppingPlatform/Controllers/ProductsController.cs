using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using System.IO;

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
            [FromQuery] string? sort = "latest")
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var (items, total) = await _repo.QueryAsync(q, category, minPrice, maxPrice, page, pageSize, sort);

            var paged = new PagedResult<ProductListItemDto>
            {
                PageNumber = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            };

            var response = new ApiResponse<PagedResult<ProductListItemDto>>
            {
                Success = true,
                Message = "OK",
                Data = paged
            };

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
                return NotFound(new ApiResponse<ProductDetailDto>
                {
                    Success = false,
                    Message = "Product not found",
                    Data = null
                });
            }

            var dto = new ProductDetailDto
            {
                Id = product.Id ?? string.Empty,
                Name = product.Name,
                Description = product.Description,
                Images = product.Images ?? new List<ProductImage>(),
                Variants = product.Variants ?? new List<ProductVariant>(),
                Prices = product.CountryPrices ?? new Dictionary<string, decimal>(),
                Colors = product.Variants?.Select(v => v.Color).Distinct().ToList() ?? new List<string>(),
                VariantSkus = product.Variants?.Select(v => v.Sku).ToList() ?? new List<string>()
            };

            return Ok(new ApiResponse<ProductDetailDto>
            {
                Success = true,
                Message = "OK",
                Data = dto
            });
        }

        // -------------------------------------------
        // POST: api/products
        // Admin only - create new product
        // -------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<Product>>> Create([FromBody] Product product)
        {
            product.Id = null;
            await _repo.CreateAsync(product);

            var resp = new ApiResponse<Product>
            {
                Success = true,
                Message = "Product created",
                Data = product
            };

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
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Product not found",
                    Data = null
                });
            }

            updated.Id = id;
            await _repo.UpdateAsync(updated);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Product updated",
                Data = null
            });
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

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Product deleted",
                Data = null
            });
        }

        // -------------------------------------------
        // GET: api/products/{id}/images/presign
        // -------------------------------------------
        [HttpGet("{id}/images/presign")]
        [Authorize(Roles = "Admin")]
        public ActionResult<ApiResponse<object>> PresignImage(string id, [FromQuery] string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Filename required" });

            var key = $"products/{id}/{Guid.NewGuid()}_{Path.GetFileName(filename)}";
            var uploadUrl = _s3.GetPreSignedUploadUrl(_aws.Bucket, key, TimeSpan.FromMinutes(10));
            var objectUrl = _s3.GetObjectUrl(_aws.Bucket, key);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Presigned URL created",
                Data = new { uploadUrl, objectUrl, key }
            });
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
                return NotFound(new ApiResponse<ProductImage> { Success = false, Message = "Product not found" });

            await _repo.AddImageAsync(id, image);
            return Ok(new ApiResponse<ProductImage> { Success = true, Message = "Image attached", Data = image });
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

            await _repo.AddReviewAsync(id, review);
            return Accepted(new ApiResponse<object> { Success = true, Message = "Review submitted for moderation", Data = null });
        }

        // -------------------------------------------
        // DELETE: api/products/{id}/images?keyOrUrl=...
        // -------------------------------------------
        [HttpDelete("{id}/images")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<object>>> DeleteImage(string id, [FromQuery] string keyOrUrl)
        {
            if (string.IsNullOrWhiteSpace(keyOrUrl))
                return BadRequest(new ApiResponse<object> { Success = false, Message = "keyOrUrl required" });

            var bucket = string.IsNullOrEmpty(_aws.Bucket) ? _aws.BucketName : _aws.Bucket;
            var deletedFromS3 = await _s3.DeleteObjectAsync(bucket, keyOrUrl);
            var removed = await _repo.RemoveImageAsync(id, keyOrUrl);
            if (!removed)
                return NotFound(new ApiResponse<object> { Success = false, Message = "Image not found in product" });

            return Ok(new ApiResponse<object> { Success = true, Message = "Image removed", Data = new { s3Deleted = deletedFromS3 } });
        }

        // -------------------------------------------
        // GET: api/products/categories
        // -------------------------------------------
        [HttpGet("categories")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<CategoryCount>>>> GetCategories()
        {
            var cats = await _repo.GetCategoriesAsync();
            return Ok(new ApiResponse<List<CategoryCount>> { Success = true, Message = "OK", Data = cats });
        }

    }

}
