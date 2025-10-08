using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Models;
using ShoppingPlatform.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly IMongoCollection<Product> _col;

        public ProductRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<Product>("Products");
            EnsureIndexes();
        }

        private void EnsureIndexes()
        {
            try
            {
                var indexKeys = Builders<Product>.IndexKeys.Ascending(p => p.Slug);
                _col.Indexes.CreateOne(new CreateIndexModel<Product>(indexKeys, new CreateIndexOptions { Unique = true }));

                var idx2 = Builders<Product>.IndexKeys.Combine(
                    Builders<Product>.IndexKeys.Ascending(p => p.Category),
                    Builders<Product>.IndexKeys.Descending(p => p.CreatedAt));
                _col.Indexes.CreateOne(new CreateIndexModel<Product>(idx2));
            }
            catch
            {
                // ignore index creation errors (e.g., in tests)
            }
        }

        /// <summary>
        /// Query products for list view. Returns DTO list and total count.
        /// </summary>
        public async Task<(List<ProductListItemDto> items, long total)> QueryAsync(string? q, string? category,
            decimal? minPrice, decimal? maxPrice, int page, int pageSize, string? sort)
        {
            var filter = Builders<Product>.Filter.Empty;

            if (!string.IsNullOrWhiteSpace(q))
            {
                var nameFilter = Builders<Product>.Filter.Regex(p => p.Name, new BsonRegularExpression(q, "i"));
                var descFilter = Builders<Product>.Filter.Regex(p => p.Description, new BsonRegularExpression(q, "i"));
                filter &= Builders<Product>.Filter.Or(nameFilter, descFilter);
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                filter &= Builders<Product>.Filter.Eq(p => p.Category, category);
            }

            // only active and not deleted
            filter &= Builders<Product>.Filter.Eq(p => p.IsActive, true) & Builders<Product>.Filter.Eq(p => p.IsDeleted, false);

            var sortDef = sort == "latest"
                ? Builders<Product>.Sort.Descending(p => p.CreatedAt)
                : Builders<Product>.Sort.Descending(p => p.SalesCount);

            var skip = Math.Max((page - 1) * pageSize, 0);

            var total = await _col.CountDocumentsAsync(filter);
            var products = await _col.Find(filter).Sort(sortDef).Skip(skip).Limit(pageSize).ToListAsync();

            // Map products to ProductListItemDto
            var items = products.Select(p =>
            {
                decimal price = p.Price;
                if (p.CountryPrices != null && p.CountryPrices.Count > 0)
                {
                    if (p.CountryPrices.TryGetValue("IN", out var inPrice))
                        price = inPrice;
                    else
                        price = p.CountryPrices.Values.First();
                }

                var images = p.Images?.Select(i => i.Url).ToList() ?? new List<string>();
                var available = p.Variants != null && p.Variants.Any(v => v.Quantity > 0);

                return new ProductListItemDto
                {
                    Id = p.Id ?? string.Empty,
                    Name = p.Name,
                    Price = price,
                    Category = p.Category,
                    Images = images,
                    Available = available
                };
            }).ToList();

            return (items, total);
        }

        public async Task<Product?> GetByIdAsync(string id)
        {
            return await _col.Find(p => p.Id == id && p.IsActive && !p.IsDeleted).FirstOrDefaultAsync();
        }

        public async Task CreateAsync(Product product)
        {
            product.Id = string.IsNullOrWhiteSpace(product.Id) ? ObjectId.GenerateNewId().ToString() : product.Id;
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;
            await _col.InsertOneAsync(product);
        }

        public async Task UpdateAsync(Product product)
        {
            product.UpdatedAt = DateTime.UtcNow;
            var res = await _col.ReplaceOneAsync(p => p.Id == product.Id, product);
            if (res.MatchedCount == 0) throw new Exception("Product not found");
        }

        public async Task DeleteAsync(string id)
        {
            var update = Builders<Product>.Update.Set(p => p.IsDeleted, true).Set(p => p.IsActive, false).Set(p => p.UpdatedAt, DateTime.UtcNow);
            await _col.UpdateOneAsync(p => p.Id == id, update);
        }

        public async Task AddImageAsync(string id, ProductImage image)
        {
            var update = Builders<Product>.Update.Push(p => p.Images, image).Set(p => p.UpdatedAt, DateTime.UtcNow);
            await _col.UpdateOneAsync(p => p.Id == id, update);
        }

        public async Task<bool> RemoveImageAsync(string id, string keyOrUrl)
        {
            var update = Builders<Product>.Update.PullFilter(p => p.Images,
                Builders<ProductImage>.Filter.Or(
                    Builders<ProductImage>.Filter.Eq(img => img.Url, keyOrUrl),
                    Builders<ProductImage>.Filter.Eq(img => img.ThumbnailUrl, keyOrUrl)
                ) as FilterDefinition<ProductImage>);

            var res = await _col.UpdateOneAsync(p => p.Id == id, update);
            return res.ModifiedCount > 0;
        }

        public async Task AddReviewAsync(string id, Review review)
        {
            var update = Builders<Product>.Update
                .Push(p => p.Reviews, review)
                .Inc(p => p.ReviewCount, 1)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            await _col.UpdateOneAsync(p => p.Id == id, update);

            // recompute approved-average
            var product = await GetByIdAsync(id);
            if (product != null)
            {
                var approved = product.Reviews?.Where(r => r.Approved).Select(r => r.Rating).ToList() ?? new List<int>();
                var avg = approved.Count > 0 ? approved.Average() : 0.0;
                var upd = Builders<Product>.Update.Set(p => p.AverageRating, avg);
                await _col.UpdateOneAsync(p => p.Id == id, upd);
            }
        }

        public async Task<List<CategoryCount>> GetCategoriesAsync()
        {
            var pipeline = new[]
            {
                new BsonDocument { { "$match", new BsonDocument { { "IsActive", true }, { "IsDeleted", false } } } },
                new BsonDocument { { "$group", new BsonDocument { { "_id", "$Category" }, { "count", new BsonDocument("$sum", 1) } } } },
                new BsonDocument { { "$project", new BsonDocument { { "Category", "$_id" }, { "Count", "$count" }, { "_id", 0 } } } }
            };

            var docs = await _col.Aggregate<BsonDocument>(pipeline).ToListAsync();

            var result = docs.Select(d => new CategoryCount
            {
                Category = d.GetValue("Category").AsString,
                Count = d.GetValue("Count").ToInt32()
            }).ToList();

            return result;
        }

        // Atomic variant decrement (safe reservation)
        public async Task<bool> TryDecrementVariantQuantityAsync(string productId, string variantSku, int qty)
        {
            if (qty <= 0) return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Variants, v => v.Sku == variantSku && v.Quantity >= qty)
            );

            var update = Builders<Product>.Update
                .Inc("Variants.$[v].Quantity", -qty)
                .Inc(p => p.SalesCount, qty)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var arrayFilter = new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("v.Sku", variantSku));
            var options = new UpdateOptions { ArrayFilters = new List<ArrayFilterDefinition> { arrayFilter } };

            var result = await _col.UpdateOneAsync(filter, update, options);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> IncrementVariantQuantityAsync(string productId, string variantSku, int qty)
        {
            if (qty <= 0) return false;

            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);
            var update = Builders<Product>.Update
                .Inc("Variants.$[v].Quantity", qty)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var arrayFilter = new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("v.Sku", variantSku));
            var options = new UpdateOptions { ArrayFilters = new List<ArrayFilterDefinition> { arrayFilter } };

            var result = await _col.UpdateOneAsync(filter, update, options);
            return result.ModifiedCount > 0;
        }

        // -----------------------
        // Review moderation
        // -----------------------
        public async Task<IEnumerable<Review>> GetPendingReviewsAsync(int page = 1, int pageSize = 50)
        {
            var filter = Builders<Product>.Filter.ElemMatch(p => p.Reviews, r => r.Approved == false);
            var products = await _col.Find(filter).ToListAsync();

            // Extract unapproved reviews and do simple paging across the flattened list
            var pending = new List<Review>();
            foreach (var p in products)
            {
                if (p.Reviews == null) continue;
                pending.AddRange(p.Reviews.Where(r => !r.Approved).Select(r =>
                {
                    // ensure the review has a reference to product id (we can use Id in comment if needed)
                    return new Review
                    {
                        Id = r.Id,
                        UserId = r.UserId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        CreatedAt = r.CreatedAt,
                        Approved = r.Approved
                    };
                }));
            }

            var skip = Math.Max((page - 1) * pageSize, 0);
            return pending.Skip(skip).Take(pageSize).ToList();
        }

        public async Task<bool> ApproveReviewAsync(string productId, string reviewId)
        {
            // Set the specific review's Approved = true using positional filtered update
            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);
            var update = Builders<Product>.Update.Set("Reviews.$[r].Approved", true);
            var arrayFilter = new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("r.Id", reviewId));
            var options = new UpdateOptions { ArrayFilters = new List<ArrayFilterDefinition> { arrayFilter } };

            var res = await _col.UpdateOneAsync(filter, update, options);
            if (res.ModifiedCount > 0)
            {
                // recompute average rating
                var product = await GetByIdAsync(productId);
                if (product != null)
                {
                    var approved = product.Reviews?.Where(r => r.Approved).Select(r => r.Rating).ToList() ?? new List<int>();
                    var avg = approved.Count > 0 ? approved.Average() : 0.0;
                    var upd = Builders<Product>.Update.Set(p => p.AverageRating, avg);
                    await _col.UpdateOneAsync(p => p.Id == productId, upd);
                }
                return true;
            }

            return false;
        }

        public async Task<bool> RejectReviewAsync(string productId, string reviewId)
        {
            // Remove the review from the product
            var update = Builders<Product>.Update.PullFilter(p => p.Reviews,
                Builders<Review>.Filter.Eq(r => r.Id, reviewId) as FilterDefinition<Review>);

            var res = await _col.UpdateOneAsync(p => p.Id == productId, update);
            return res.ModifiedCount > 0;
        }
    }
}
