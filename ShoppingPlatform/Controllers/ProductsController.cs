using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
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

        [HttpGet]
        public async Task<IActionResult> Query([FromQuery] string? q, [FromQuery] string? category,
            [FromQuery] decimal? minPrice = null, [FromQuery] decimal? maxPrice = null,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? sort = null)
        {
            var (items, total) = await _repo.QueryAsync(q, category, minPrice, maxPrice, page, pageSize, sort);
            return Ok(new { items, total, page, pageSize });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var p = await _repo.GetByIdAsync(id);
            if (p == null) return NotFound();
            return Ok(p);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] Product product)
        {
            product.Id = null;
            await _repo.CreateAsync(product);
            return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(string id, [FromBody] Product product)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) return NotFound();

            product.Id = id;
            await _repo.UpdateAsync(product);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            await _repo.DeleteAsync(id);
            return NoContent();
        }

        [HttpGet("{id}/images/presign")]
        [Authorize(Roles = "Admin")]
        public IActionResult PresignImage(string id, [FromQuery] string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return BadRequest("Filename required");

            var key = $"products/{id}/{Guid.NewGuid()}_{Path.GetFileName(filename)}";
            var uploadUrl = _s3.GetPreSignedUploadUrl(_aws.Bucket, key, TimeSpan.FromMinutes(10));
            var objectUrl = _s3.GetObjectUrl(_aws.Bucket, key);

            return Ok(new { uploadUrl, objectUrl, key });
        }

        [HttpPost("{id}/images")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AttachImage(string id, [FromBody] ProductImage image)
        {
            var product = await _repo.GetByIdAsync(id);
            if (product == null) return NotFound();

            await _repo.AddImageAsync(id, image);
            return Ok(image);
        }

        [HttpPost("{id}/reviews")]
        [Authorize]
        public async Task<IActionResult> AddReview(string id, [FromBody] Review review)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
            review.UserId = userId;
            review.CreatedAt = DateTime.UtcNow;

            await _repo.AddReviewAsync(id, review);
            return Accepted(new { message = "Review submitted for moderation" });
        }

        [HttpDelete("{id}/images")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteImage(string id, [FromQuery] string keyOrUrl)
        {
            if (string.IsNullOrWhiteSpace(keyOrUrl)) return BadRequest("keyOrUrl required");

            // Try to delete from S3 first (best effort)
            var bucket = string.IsNullOrEmpty(_aws.Bucket) ? _aws.BucketName : _aws.Bucket;
            var deletedFromS3 = await _s3.DeleteObjectAsync(bucket, keyOrUrl);

            // Remove metadata from product document
            var removed = await _repo.RemoveImageAsync(id, keyOrUrl);
            if (!removed)
            {
                // if DB remove failed but S3 succeeded, you might log; return NotFound to indicate missing DB entry
                return NotFound(new { message = "Image entry not found on product" });
            }

            return Ok(new { message = "Image removed", s3Deleted = deletedFromS3 });
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var cats = await _repo.GetCategoriesAsync();
            return Ok(cats.Select(c => new { c.Category, c.Count }));
        }

    }
}
