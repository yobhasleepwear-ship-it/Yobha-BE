using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClosedXML.Excel;
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
        [HttpPost("Query")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PagedResult<ProductListItemDto>>>> Query(
    [FromBody] ProductQueryRequest req
)
        {
            if (req.page <= 0) req.page = 1;
            if (req.pageSize <= 0) req.pageSize = 20;

            var (items, total) = await _repo.QueryAsync(req.q, req.category, req.subCategory, req.minPrice, req.maxPrice, req.fabric, req.page, req.pageSize, req.sort, req.country,req.colors, req.sizes);

            var paged = new PagedResult<ProductListItemDto>
            {
                Page = req.page,
                PageSize = req.pageSize,
                TotalCount = total,
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
        public async Task<ActionResult<ApiResponse<Product>>> Get(string id)
        {
            var product = await _repo.GetByIdAsync(id);
            if (product == null)
            {
                var notFound = ApiResponse<Product>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

           // var dto = ProductMappings.ToDetailDto(product);

            var ok = ApiResponse<Product>.Ok(product, "OK");
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
            if (product == null)
            {
                return BadRequest(ApiResponse<Product>.Fail("Product body required", null, HttpStatusCode.BadRequest));
            }

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

            // Ensure nested IDs exist
            EnsureEntityIds(product);

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
            if (updated == null)
            {
                return BadRequest(ApiResponse<object>.Fail("Product body required", null, HttpStatusCode.BadRequest));
            }

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

            EnsureEntityIds(updated);

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
        public async Task<ActionResult<ApiResponse<ProductImage>>> AttachImage(string id, [FromBody] ProductImageRequest imageReq)
        {
            if (imageReq == null)
                return BadRequest(ApiResponse<ProductImage>.Fail("Image payload required", null, HttpStatusCode.BadRequest));

            var product = await _repo.GetByIdAsync(id);
            if (product == null)
            {
                var notFound = ApiResponse<ProductImage>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            var image = new ProductImage
            {
                Url = imageReq.Url,
                ThumbnailUrl = imageReq.ThumbnailUrl,
                Alt = imageReq.Alt,
                UploadedByUserId = imageReq.UploadedByUserId,
                UploadedAt = DateTime.UtcNow
            };

            // Ensure image fields valid
            await _repo.AddImageAsync(id, image);

            var ok = ApiResponse<ProductImage>.Ok(image, "Image attached");
            return Ok(ok);
        }

        // -------------------------------------------
        // POST: api/products/{id}/reviews
        // -------------------------------------------
        [HttpPost("{id}/reviews")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> AddReview(string id, [FromBody] ReviewRequest reviewReq)
        {
            if (reviewReq == null)
                return BadRequest(ApiResponse<object>.Fail("Review required", null, HttpStatusCode.BadRequest));

            var product = await _repo.GetByIdAsync(id);
            if (product == null)
            {
                var notFound = ApiResponse<object>.Fail("Product not found", null, HttpStatusCode.NotFound);
                return NotFound(notFound);
            }

            var userId = User.GetUserIdOrAnonymous();

            var review = new Review
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Rating = reviewReq.Rating,
                Comment = reviewReq.Comment,
                CreatedAt = DateTime.UtcNow,
                Approved = false
            };

            await _repo.AddReviewAsync(id, review);

            var accepted = ApiResponse<object>.Ok(null, "Review submitted for moderation");
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

        [HttpPost("bulk-upload")]
        [AllowAnonymous]
        public async Task<IActionResult> BulkUpload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Excel file required");

            var products = new List<Product>();
            var errors = new List<object>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet("Products");
            var rows = sheet.RowsUsed().Skip(1);

            int rowNumber = 1;

            foreach (var row in rows)
            {
                rowNumber++;

                try
                {
                    var product = BuildProduct(row);
                    products.Add(product);
                }
                catch (Exception ex)
                {
                    errors.Add(new
                    {
                        Row = rowNumber,
                        ProductId = row.Cell(1).GetString(),
                        Error = ex.Message
                    });
                }
            }

            if (products.Any())
                await _repo.InsertManyAsync(products);

            return Ok(new
            {
                Total = products.Count + errors.Count,
                Inserted = products.Count,
                Failed = errors.Count,
                Errors = errors
            });
        }

        private Product BuildProduct(IXLRow row)
        {
            return new Product
            {
                ProductId = row.Cell("A").GetString(),
                Name = row.Cell("B").GetString(),
                Slug = row.Cell("C").GetString(),
                Description = row.Cell("D").GetString(),

                ProductMainCategory = row.Cell("E").GetString(),
                ProductCategory = row.Cell("F").GetString(),
                ProductSubCategory = row.Cell("G").GetString(),

                IsActive = row.Cell("H").GetBoolean(),
                IsFeatured = row.Cell("I").GetBoolean(),

                SizeOfProduct = SplitList(row.Cell("J").GetString()),
                AvailableColors = SplitList(row.Cell("K").GetString()),
                FabricType = SplitList(row.Cell("L").GetString()),

                Images = ParseImages(row.Cell("M").GetString()),
                PriceList = ParsePriceList(row.Cell("N").GetString()),
                CountryPrices = ParseCountryPrices(row.Cell("O").GetString()),

                Specifications = new ProductSpecifications
                {
                    Fabric = row.Cell("P").GetString(),
                    Length = row.Cell("Q").GetString(),
                    Origin = row.Cell("R").GetString(),
                    Fit = row.Cell("S").GetString(),
                    Care = row.Cell("T").GetString(),
                    Extra = ParseExtraSpecs(row.Cell("U").GetString())
                },

                KeyFeatures = SplitList(row.Cell("V").GetString()),
                CareInstructions = SplitList(row.Cell("W").GetString()),

                FreeDelivery = row.Cell("X").GetBoolean(),
                ReturnPolicy = row.Cell("Y").GetString(),

                ShippingInfo = new ShippingInfo
                {
                    FreeShipping = row.Cell("Z").GetBoolean(),
                    EstimatedDelivery = row.Cell("AA").GetString(),
                    CashOnDelivery = row.Cell("AB").GetBoolean()
                }
            };
        }

        List<string> SplitList(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? new()
        : value.Split('|').Select(x => x.Trim()).ToList();

        List<ProductImage> ParseImages(string value) =>
            SplitList(value).Select(u => new ProductImage { Url = u }).ToList();

        List<Price> ParsePriceList(string value)
        {
            return SplitList(value).Select(p =>
            {
                var x = p.Split(':');
                return new Price
                {
                    Size = x[0],
                    Country = x[1],
                    PriceAmount = decimal.Parse(x[2]),
                    Currency = x[3]
                };
            }).ToList();
        }

        List<CountryPrice> ParseCountryPrices(string value)
        {
            return SplitList(value).Select(p =>
            {
                var x = p.Split(':');
                return new CountryPrice
                {
                    Country = x[0],
                    PriceAmount = decimal.Parse(x[1]),
                    Currency = x[2]
                };
            }).ToList();
        }

        List<SpecificationField> ParseExtraSpecs(string value)
        {
            return SplitList(value).Select(e =>
            {
                var x = e.Split(':');
                return new SpecificationField { Key = x[0], Value = x[1] };
            }).ToList();
        }


        // -------------------------
        // Helper: ensure nested ids exist (PriceList, CountryPrices, Inventory, Variants, Spec extra, Reviews, Images)
        // -------------------------
        private void EnsureEntityIds(Product product)
        {
            if (product.PriceList != null)
            {
                foreach (var p in product.PriceList)
                {
                    if (string.IsNullOrWhiteSpace(p.Id))
                        p.Id = Guid.NewGuid().ToString("N");
                }
            }

            if (product.CountryPrices != null)
            {
                foreach (var cp in product.CountryPrices)
                {
                    if (string.IsNullOrWhiteSpace(cp.Id))
                        cp.Id = Guid.NewGuid().ToString("N");
                }
            }

            if (product.Inventory != null)
            {
                foreach (var inv in product.Inventory)
                {
                    if (string.IsNullOrWhiteSpace(inv.Id))
                        inv.Id = Guid.NewGuid().ToString("N");
                    inv.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (product.Variants != null)
            {
                foreach (var v in product.Variants)
                {
                    if (string.IsNullOrWhiteSpace(v.Id))
                        v.Id = Guid.NewGuid().ToString("N");
                    if (v.Images != null)
                    {
                        foreach (var img in v.Images)
                        {
                            if (img.UploadedAt == default)
                                img.UploadedAt = DateTime.UtcNow;
                        }
                    }
                }
            }

            if (product.Specifications?.Extra != null)
            {
                foreach (var f in product.Specifications.Extra)
                {
                    if (string.IsNullOrWhiteSpace(f.Id))
                        f.Id = Guid.NewGuid().ToString("N");
                }
            }

            if (product.Reviews != null)
            {
                foreach (var r in product.Reviews)
                {
                    if (string.IsNullOrWhiteSpace(r.Id))
                        r.Id = Guid.NewGuid().ToString("N");
                    if (r.CreatedAt == default)
                        r.CreatedAt = DateTime.UtcNow;
                }
            }

            if (product.Images != null)
            {
                foreach (var img in product.Images)
                {
                    if (img.UploadedAt == default)
                        img.UploadedAt = DateTime.UtcNow;
                }
            }
        }
    }

    // -------------------------
    // Small request/response DTOs used by controller
    // -------------------------
    public class ProductImageRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Alt { get; set; }
        public string? UploadedByUserId { get; set; }
    }

    public class ReviewRequest
    {
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
    }
}
