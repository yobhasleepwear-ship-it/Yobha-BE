using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace ShoppingPlatform.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly IMongoCollection<Product> _col;

        public ProductRepository(IMongoClient client, IOptions<MongoDbSettings> mongoSettings)
        {
            var db = client.GetDatabase(mongoSettings.Value.DatabaseName);
            _col = db.GetCollection<Product>("products");
        }

        // -------------------------
        // Basic CRUD
        // -------------------------
        public async Task CreateAsync(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            await _col.InsertOneAsync(product);
        }

        public async Task<Product?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return await _col.Find(p => p.Id == id).FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            await _col.ReplaceOneAsync(p => p.Id == product.Id, product, new ReplaceOptions { IsUpsert = false });
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            await _col.DeleteOneAsync(p => p.Id == id);
        }

        // -------------------------
        // Query / Pagination / Sorting
        // -------------------------
        public async Task<(IEnumerable<Product> Items, long Total)> QueryAsync(string? q, string? category, decimal? minPrice, decimal? maxPrice, int page, int pageSize, string? sort)
        {
            var builder = Builders<Product>.Filter;
            var filters = new List<FilterDefinition<Product>>();

            if (!string.IsNullOrWhiteSpace(q))
            {
                // assume text index exists on relevant fields
                filters.Add(builder.Text(q));
            }

            if (!string.IsNullOrWhiteSpace(category))
                filters.Add(builder.Eq(p => p.Category, category));

            if (minPrice.HasValue)
                filters.Add(builder.Gte(p => p.Price, minPrice.Value));

            if (maxPrice.HasValue)
                filters.Add(builder.Lte(p => p.Price, maxPrice.Value));

            var finalFilter = filters.Any() ? builder.And(filters) : builder.Empty;
            var find = _col.Find(finalFilter);

            if (!string.IsNullOrWhiteSpace(sort))
            {
                switch (sort.ToLowerInvariant())
                {
                    case "best-sellers":
                        find = find.SortByDescending(p => p.SalesCount);
                        break;
                    case "price-asc":
                        find = find.SortBy(p => p.Price);
                        break;
                    case "price-desc":
                        find = find.SortByDescending(p => p.Price);
                        break;
                    case "rating":
                        find = find.SortByDescending(p => p.AverageRating);
                        break;
                    default:
                        find = find.SortByDescending(p => p.IsFeatured);
                        break;
                }
            }
            else
            {
                find = find.SortByDescending(p => p.IsFeatured);
            }

            var total = await find.CountDocumentsAsync();
            var items = await find.Skip((Math.Max(page, 1) - 1) * pageSize).Limit(pageSize).ToListAsync();

            return (items, total);
        }

        // -------------------------
        // Images & Reviews (add only)
        // -------------------------
        public async Task AddImageAsync(string productId, ProductImage image)
        {
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentNullException(nameof(productId));
            if (image == null) throw new ArgumentNullException(nameof(image));

            await _col.UpdateOneAsync(
                p => p.Id == productId,
                Builders<Product>.Update.Push(p => p.Images, image)
            );
        }

        public async Task AddReviewAsync(string productId, Review review)
        {
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentNullException(nameof(productId));
            if (review == null) throw new ArgumentNullException(nameof(review));

            // Ensure review has Id
            if (string.IsNullOrWhiteSpace(review.Id))
                review.Id = Guid.NewGuid().ToString();

            await _col.UpdateOneAsync(
                p => p.Id == productId,
                Builders<Product>.Update.Push(p => p.Reviews, review)
            );

            // After insertion, recalc aggregates based on approved reviews only
            await RecalculateAggregatesAsync(productId);
        }

        // -------------------------
        // Moderation
        // -------------------------
        public async Task<IEnumerable<PendingReview>> GetPendingReviewsAsync(int page = 1, int pageSize = 50)
        {
            var filter = Builders<Product>.Filter.ElemMatch(p => p.Reviews, r => r.Approved == false);
            var products = await _col.Find(filter).Skip(Math.Max(page - 1, 0) * pageSize).Limit(pageSize).ToListAsync();

            var pending = new List<PendingReview>();
            foreach (var p in products)
            {
                if (p.Reviews == null) continue;
                foreach (var r in p.Reviews.Where(x => !x.Approved))
                {
                    pending.Add(new PendingReview
                    {
                        ProductId = p.Id,
                        ProductName = p.Name,
                        ReviewId = r.Id,
                        UserId = r.UserId,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        CreatedAt = r.CreatedAt
                    });
                }
            }
            return pending;
        }

        public async Task<bool> ApproveReviewAsync(string productId, string reviewId)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(reviewId)) return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Reviews, r => r.Id == reviewId)
            );

            var update = Builders<Product>.Update.Set("Reviews.$.Approved", true);
            var res = await _col.UpdateOneAsync(filter, update);

            if (res.ModifiedCount == 0) return false;

            await RecalculateAggregatesAsync(productId);
            return true;
        }

        public async Task<bool> RejectReviewAsync(string productId, string reviewId)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(reviewId)) return false;

            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);
            var update = Builders<Product>.Update.PullFilter(p => p.Reviews, r => r.Id == reviewId);

            var res = await _col.UpdateOneAsync(filter, update);
            if (res.ModifiedCount == 0) return false;

            await RecalculateAggregatesAsync(productId);
            return true;
        }

        public async Task<bool> RemoveImageAsync(string productId, string imageUrlOrKey)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(imageUrlOrKey)) return false;

            // Remove image by matching Url field (or you can store/compare the key)
            var update = Builders<Product>.Update.PullFilter(p => p.Images, img => img.Url == imageUrlOrKey);
            var res = await _col.UpdateOneAsync(p => p.Id == productId, update);
            return res.ModifiedCount > 0;
        }

        public async Task<IEnumerable<(string Category, long Count)>> GetCategoriesAsync()
        {
            var group = new BsonDocument
    {
        { "$group", new BsonDocument { { "_id", "$Category" }, { "count", new BsonDocument { { "$sum", 1 } } } } }
    };

            var sort = new BsonDocument
    {
        { "$sort", new BsonDocument { { "count", -1 } } }
    };

            var pipeline = new[] { group, sort };
            var result = await _col.Aggregate<BsonDocument>(pipeline).ToListAsync();

            return result.Select(b => (Category: b["_id"].AsString, Count: b["count"].AsInt64));
        }

        // -------------------------
        // Helper: recompute review aggregates (only approved reviews)
        // -------------------------
        private async Task RecalculateAggregatesAsync(string productId)
        {
            var product = await _col.Find(p => p.Id == productId).FirstOrDefaultAsync();
            if (product == null) return;

            var approved = product.Reviews?.Where(r => r.Approved).ToList() ?? new List<Review>();
            var count = approved.Count;
            var avg = count > 0 ? approved.Average(r => r.Rating) : 0.0;

            var update = Builders<Product>.Update
                .Set(p => p.AverageRating, avg)
                .Set(p => p.ReviewCount, count);

            await _col.UpdateOneAsync(p => p.Id == productId, update);
        }
    }
}
