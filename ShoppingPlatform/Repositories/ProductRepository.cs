using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Dto;
using ShoppingPlatform.Helpers;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly IMongoCollection<Product> _collection;

        public ProductRepository(IMongoDatabase database, string collectionName = "products")
        {
            _collection = database.GetCollection<Product>(collectionName);
        }

        // -----------------------
        // QueryAsync
        // -----------------------
        public async Task<(List<ProductListItemDto> items, long total)> QueryAsync(string? q, string? category, string? subCategory,
            decimal? minPrice, decimal? maxPrice,List<string>? fabric, int page, int pageSize, string? sort)
        {
            var filters = new List<FilterDefinition<Product>>();
            var builder = Builders<Product>.Filter;

            // Only active and not deleted by default
            filters.Add(builder.Eq(p => p.IsActive, true));
            filters.Add(builder.Eq(p => p.IsDeleted, false));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qLower = q.Trim();
                var nameFilter = builder.Regex(p => p.Name, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                var descFilter = builder.Regex(p => p.Description, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                var slugFilter = builder.Regex(p => p.Slug, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                filters.Add(builder.Or(nameFilter, descFilter, slugFilter));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                var catFilter = builder.Or(
                    builder.Eq(p => p.ProductCategory, cat),
                    builder.Eq(p => p.ProductSubCategory, cat),
                    builder.Eq(p => p.ProductMainCategory, cat)
                );
                filters.Add(catFilter);
            }

            if (!string.IsNullOrWhiteSpace(subCategory))
            {
                var subCat = subCategory.Trim();
                var subCatFilter = builder.Eq(p => p.ProductCategory, subCat);
                filters.Add(subCatFilter);
            }

            if (fabric != null && fabric.Any())
            {
                var fabricfilter = builder.AnyIn(p => p.FabricType, fabric);
                filters.Add(fabricfilter);
            }

            var combinedFilter = filters.Count > 0 ? builder.And(filters) : builder.Empty;

            var total = await _collection.CountDocumentsAsync(combinedFilter);

            var sortDefBuilder = Builders<Product>.Sort;
            SortDefinition<Product> sortDef = sort switch
            {
                "popular" => sortDefBuilder.Descending(p => p.SalesCount),
                "price_asc" => sortDefBuilder.Ascending(p => p.Price),
                "price_desc" => sortDefBuilder.Descending(p => p.Price),
                _ => sortDefBuilder.Descending(p => p.CreatedAt)
            };

            var skip = (page - 1) * pageSize;
            var productsCursor = await _collection.Find(combinedFilter)
                .Sort(sortDef)
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync();

            var mapped = productsCursor.Select(p => ProductMappings.ToListItemDto(p)).ToList();

            if (minPrice.HasValue || maxPrice.HasValue)
            {
                mapped = mapped.Where(item =>
                {
                    var price = item.Price;
                    if (minPrice.HasValue && price < minPrice.Value) return false;
                    if (maxPrice.HasValue && price > maxPrice.Value) return false;
                    return true;
                }).ToList();
            }

            return (mapped, total);
        }


        public async Task<(List<ProductListItemDto> items, long total)> QueryAsync(
            string? q,
            string? category,
            string? subCategory,
            decimal? minPrice,
            decimal? maxPrice,
            List<string>? fabric,
            int page,
            int pageSize,
            string? sort,
            string? country // new param
        )
        {
            var builder = Builders<Product>.Filter;
            var filters = new List<FilterDefinition<Product>>();

            // Base filters
            filters.Add(builder.Eq(p => p.IsActive, true));
            filters.Add(builder.Eq(p => p.IsDeleted, false));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qLower = q.Trim();
                var nameFilter = builder.Regex(p => p.Name, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                var descFilter = builder.Regex(p => p.Description, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                var slugFilter = builder.Regex(p => p.Slug, new MongoDB.Bson.BsonRegularExpression(qLower, "i"));
                filters.Add(builder.Or(nameFilter, descFilter, slugFilter));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                var catFilter = builder.Or(
                    builder.Eq(p => p.ProductCategory, cat),
                    builder.Eq(p => p.ProductSubCategory, cat),
                    builder.Eq(p => p.ProductMainCategory, cat)
                );
                filters.Add(catFilter);
            }

            if (!string.IsNullOrWhiteSpace(subCategory))
            {
                var subCat = subCategory.Trim();
                var subCatFilter = builder.Eq(p => p.ProductCategory, subCat);
                filters.Add(subCatFilter);
            }

            if (fabric != null && fabric.Any())
            {
                var fabricfilter = builder.AnyIn(p => p.FabricType, fabric);
                filters.Add(fabricfilter);
            }

            // PRICE FILTER: Only apply on PriceList (not CountryPrices)
            if (minPrice.HasValue || maxPrice.HasValue)
            {
                var priceInnerFilters = new List<FilterDefinition<Price>>();

                // If country provided, require PriceList.Country == country for the matched element
                if (!string.IsNullOrWhiteSpace(country))
                {
                    priceInnerFilters.Add(Builders<Price>.Filter.Eq(p => p.Country, country));
                }

                if (minPrice.HasValue)
                {
                    priceInnerFilters.Add(Builders<Price>.Filter.Gte(p => p.PriceAmount, minPrice.Value));
                }

                if (maxPrice.HasValue)
                {
                    priceInnerFilters.Add(Builders<Price>.Filter.Lte(p => p.PriceAmount, maxPrice.Value));
                }

                if (priceInnerFilters.Count > 0)
                {
                    var combinedPriceInner = Builders<Price>.Filter.And(priceInnerFilters);
                    var elemMatch = builder.ElemMatch(p => p.PriceList, combinedPriceInner);
                    filters.Add(elemMatch);
                }
            }

            var combinedFilter = filters.Count > 0 ? builder.And(filters) : builder.Empty;

            // total count using same filter
            var total = await _collection.CountDocumentsAsync(combinedFilter);

            // If sorting by price, we need an aggregation pipeline that computes an effectivePrice from PriceList
            var skip = (page - 1) * pageSize;

            // Determine if we should use price-based sorting
            var sortDef = sort ?? string.Empty;
            var usePriceSort = sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase)
                            || sortDef.Equals("price_desc", StringComparison.OrdinalIgnoreCase);

            if (usePriceSort)
            {
                // Build aggregation pipeline:
                // 1. $match combinedFilter
                // 2. $addFields: effectivePrice = min( mapped PriceList.PriceAmount after optionally filtering by country )
                // 3. $sort by effectivePrice (asc/desc)
                // 4. $skip / $limit
                // We'll keep the whole product document and map later.

                // Build Bson for country filter logic inside $filter
                BsonDocument priceFilterExpr;
                if (!string.IsNullOrWhiteSpace(country))
                {
                    // Filter PriceList where Country == country
                    priceFilterExpr = new BsonDocument("$filter", new BsonDocument
            {
                { "input", "$PriceList" },
                { "as", "pl" },
                { "cond", new BsonDocument("$eq", new BsonArray { "$$pl.Country", country }) }
            });
                }
                else
                {
                    // No country: use whole PriceList
                    priceFilterExpr = new BsonDocument("$ifNull", new BsonArray { "$PriceList", new BsonArray() });
                }

                // Map filtered PriceList to array of PriceAmount values (if empty -> null)
                var mapExpr = new BsonDocument("$map", new BsonDocument
        {
            { "input", priceFilterExpr },
            { "as", "p" },
            { "in", "$$p.PriceAmount" }
        });

                // effectivePrice: take $min of the mapped array (if empty, it will be null)
                var addFieldsStage = new BsonDocument("$addFields", new BsonDocument
        {
            { "effectivePrice", new BsonDocument("$min", mapExpr) }
        });

                // match stage
                var matchStage = new BsonDocument("$match", combinedFilter.ToBsonDocument());

                // sort stage (handle nulls by leaving them; they will sort after numbers)
                var sortDirection = sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                var sortStage = new BsonDocument("$sort", new BsonDocument("effectivePrice", sortDirection));

                // In addition to price sort, to keep deterministic order for equal prices, also sort by CreatedAt desc
                var sortStageCombined = new BsonDocument("$sort", new BsonDocument
        {
            { "effectivePrice", sortDirection },
            { "CreatedAt", -1 }
        });

                var pipeline = new[]
                {
            matchStage,
            addFieldsStage,
            sortStageCombined,
            new BsonDocument("$skip", skip),
            new BsonDocument("$limit", pageSize)
        };

                var agg = _collection.Aggregate().AppendStage<Product>(pipeline[0]);
                for (int i = 1; i < pipeline.Length; i++)
                {
                    agg = agg.AppendStage<Product>(pipeline[i]);
                }

                var productsCursor = await agg.ToListAsync();
                var mapped = productsCursor.Select(p => ProductMappings.ToListItemDto(p)).ToList();
                return (mapped, total);
            }
            else
            {
                // Not sorting by price: do simple find with the same combinedFilter and the existing sort mappings.
                var sortDefBuilder = Builders<Product>.Sort;
                SortDefinition<Product> sortDefFinal = sort switch
                {
                    "popular" => sortDefBuilder.Descending(p => p.SalesCount),
                    "price_asc" => sortDefBuilder.Ascending(p => p.Price),    // legacy: top-level Price (kept, but user asked price-list sort only when requested)
                    "price_desc" => sortDefBuilder.Descending(p => p.Price),
                    _ => sortDefBuilder.Descending(p => p.CreatedAt)
                };

                var productsCursor = await _collection.Find(combinedFilter)
                    .Sort(sortDefFinal)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync();

                var mapped = productsCursor.Select(p => ProductMappings.ToListItemDto(p)).ToList();
                return (mapped, total);
            }
        }




        // -----------------------
        // GetById / GetByProductId / Exists
        // -----------------------
        public async Task<Product?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var filter = Builders<Product>.Filter.Eq(p => p.Id, id);
            var result = await _collection.Find(filter).FirstOrDefaultAsync();
            return result;
        }

        public async Task<Product?> GetByProductIdAsync(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return null;
            var filter = Builders<Product>.Filter.Eq(p => p.ProductId, productId);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> ExistsByProductIdAsync(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return false;
            var filter = Builders<Product>.Filter.Eq(p => p.ProductId, productId);
            return await _collection.Find(filter).Limit(1).AnyAsync();
        }

        // -----------------------
        // Create / Update / Delete
        // -----------------------
        public async Task CreateAsync(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            // Check for existing product with same ProductId
            var existingProduct = await _collection
                .Find(p => p.ProductId == product.ProductId)
                .FirstOrDefaultAsync();

            if (existingProduct != null)
                throw new InvalidOperationException($"A product with ProductId '{product.ProductId}' already exists.");

            await _collection.InsertOneAsync(product);
        }


        public async Task UpdateAsync(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            // Check for existing product with same ProductId but different _id
            var duplicateProduct = await _collection
                .Find(p => p.ProductId == product.ProductId && p.Id != product.Id)
                .FirstOrDefaultAsync();

            if (duplicateProduct != null)
                throw new InvalidOperationException($"Another product with ProductId '{product.ProductId}' already exists.");

            var filter = Builders<Product>.Filter.Eq(p => p.Id, product.Id);
            await _collection.ReplaceOneAsync(filter, product);
        }


        public async Task DeleteAsync(string id)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Id, id);
            var update = Builders<Product>.Update
                .Set(p => p.IsDeleted, true)
                .Set(p => p.IsActive, false)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);
            await _collection.UpdateOneAsync(filter, update);
        }

        // -----------------------
        // Images & Reviews
        // -----------------------
        public async Task AddImageAsync(string id, ProductImage image)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Id, id);
            var update = Builders<Product>.Update.Push(p => p.Images, image).Set(p => p.UpdatedAt, DateTime.UtcNow);
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task<bool> RemoveImageAsync(string id, string keyOrUrl)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Id, id);
            var pull = Builders<Product>.Update.PullFilter(p => p.Images,
                Builders<ProductImage>.Filter.Or(
                    Builders<ProductImage>.Filter.Eq(i => i.Url, keyOrUrl),
                    Builders<ProductImage>.Filter.Eq(i => i.ThumbnailUrl, keyOrUrl)
                ))
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, pull);
            return result.ModifiedCount > 0;
        }

        public async Task AddReviewAsync(string id, Review review)
        {
            if (review == null) throw new ArgumentNullException(nameof(review));
            var filter = Builders<Product>.Filter.Eq(p => p.Id, id);
            var update = Builders<Product>.Update.Push(p => p.Reviews, review)
                .Inc(p => p.ReviewCount, 1)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);
            await _collection.UpdateOneAsync(filter, update);
        }

        // -----------------------
        // Category aggregation (fixed: no legacy Category reference)
        // -----------------------
        public async Task<List<CategoryCount>> GetCategoriesAsync()
        {
            // prefer ProductMainCategory if present; otherwise fallback to ProductCategory
            var projected = _collection.Aggregate()
                .Project(p => new
                {
                    Category = string.IsNullOrEmpty(p.ProductMainCategory) ? p.ProductCategory : p.ProductMainCategory
                });

            var grouped = await projected
                .Group(
                    key => key.Category,
                    g => new
                    {
                        Category = g.Key,
                        Count = g.Count()    // <-- use Count() method
                    })
                .ToListAsync();

            var list = grouped
                .Select(g => new CategoryCount { Category = g.Category ?? string.Empty, Count = g.Count })
                .ToList();

            return list;
        }

        // -----------------------
        // INVENTORY-BASED operations
        // -----------------------

        public async Task<bool> IsAvailableAsync(string productId, string size, string color, int requiredQty = 1)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(color))
                return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Inventory,
                    Builders<InventoryItem>.Filter.And(
                        Builders<InventoryItem>.Filter.Eq(i => i.Size, size),
                        Builders<InventoryItem>.Filter.Eq(i => i.Color, color),
                        Builders<InventoryItem>.Filter.Gte(i => i.Quantity, requiredQty)
                    )
                )
            );

            var projection = Builders<Product>.Projection.Include(p => p.Id);
            var doc = await _collection.Find(filter).Project<Product>(projection).FirstOrDefaultAsync();
            return doc != null;
        }

        public async Task<bool> TryReserveInventoryAsync(string productId, string size, string color, int qtyToReserve)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(color))
                return false;
            if (qtyToReserve <= 0) return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Inventory,
                    Builders<InventoryItem>.Filter.And(
                        Builders<InventoryItem>.Filter.Eq(i => i.Size, size),
                        Builders<InventoryItem>.Filter.Eq(i => i.Color, color),
                        Builders<InventoryItem>.Filter.Gte(i => i.Quantity, qtyToReserve)
                    )
                )
            );

            var update = Builders<Product>.Update
                .Inc("inventory.$.quantity", -qtyToReserve)
                .Inc("inventory.$.reserved", qtyToReserve)
                .Set("inventory.$.updatedAt", DateTime.UtcNow)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> ReleaseReservationAsync(string productId, string size, string color, int qty)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(color))
                return false;
            if (qty <= 0) return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Inventory,
                    Builders<InventoryItem>.Filter.And(
                        Builders<InventoryItem>.Filter.Eq(i => i.Size, size),
                        Builders<InventoryItem>.Filter.Eq(i => i.Color, color),
                        Builders<InventoryItem>.Filter.Gte(i => i.Reserved, qty)
                    )
                )
            );

            var update = Builders<Product>.Update
                .Inc("inventory.$.quantity", qty)
                .Inc("inventory.$.reserved", -qty)
                .Set("inventory.$.updatedAt", DateTime.UtcNow)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DecrementInventoryAsync(string productId, string size, string color, int qty)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(color))
                return false;
            if (qty <= 0) return false;

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Inventory,
                    Builders<InventoryItem>.Filter.And(
                        Builders<InventoryItem>.Filter.Eq(i => i.Size, size),
                        Builders<InventoryItem>.Filter.Eq(i => i.Color, color),
                        Builders<InventoryItem>.Filter.Gte(i => i.Quantity, qty)
                    )
                )
            );

            var update = Builders<Product>.Update
                .Inc("inventory.$.quantity", -qty)
                .Set("inventory.$.updatedAt", DateTime.UtcNow)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> IncrementInventoryAsync(string productId, string size, string color, int qty)
        {
            if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(size) || string.IsNullOrWhiteSpace(color))
                return false;
            if (qty <= 0) return false;

            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);

            var updateExisting = Builders<Product>.Update
                .Inc("inventory.$[inv].quantity", qty)
                .Set("inventory.$[inv].updatedAt", DateTime.UtcNow)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var arrayFilters = new List<ArrayFilterDefinition>
            {
                new JsonArrayFilterDefinition<InventoryItem>("{ 'inv.size': '" + size + "', 'inv.color': '" + color + "' }")
            };

            var options = new UpdateOptions { ArrayFilters = arrayFilters };

            var res = await _collection.UpdateOneAsync(filter, updateExisting, options);
            if (res.ModifiedCount > 0) return true;

            var newItem = new InventoryItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Size = size,
                Color = color,
                Quantity = qty,
                Reserved = 0,
                UpdatedAt = DateTime.UtcNow
            };

            var push = Builders<Product>.Update.Push(p => p.Inventory, newItem).Set(p => p.UpdatedAt, DateTime.UtcNow);
            var pushRes = await _collection.UpdateOneAsync(filter, push);
            return pushRes.ModifiedCount > 0;
        }

        // -----------------------
        // Variant wrappers
        // -----------------------
        public async Task<bool> TryDecrementVariantQuantityAsync(string productId, string variantSku, int qty)
        {
            var product = await GetByIdAsync(productId);
            if (product == null) return false;

            var variant = product.Variants?.FirstOrDefault(v => string.Equals(v.Sku, variantSku, StringComparison.OrdinalIgnoreCase));
            if (variant != null && !string.IsNullOrWhiteSpace(variant.Size) && !string.IsNullOrWhiteSpace(variant.Color))
            {
                return await DecrementInventoryAsync(productId, variant.Size, variant.Color, qty);
            }

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productId),
                Builders<Product>.Filter.ElemMatch(p => p.Variants, v => v.Sku == variantSku && v.Quantity >= qty)
            );

            var update = Builders<Product>.Update
                .Inc("variants.$[v].quantity", -qty)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var arrayFilters = new List<ArrayFilterDefinition>
            {
                new JsonArrayFilterDefinition<ProductVariant>("{ 'v.sku': '" + variantSku + "' }")
            };

            var updateOptions = new UpdateOptions { ArrayFilters = arrayFilters };

            var result = await _collection.UpdateOneAsync(filter, update, updateOptions);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> IncrementVariantQuantityAsync(string productId, string variantSku, int qty)
        {
            var product = await GetByIdAsync(productId);
            if (product == null) return false;

            var variant = product.Variants?.FirstOrDefault(v => string.Equals(v.Sku, variantSku, StringComparison.OrdinalIgnoreCase));
            if (variant != null && !string.IsNullOrWhiteSpace(variant.Size) && !string.IsNullOrWhiteSpace(variant.Color))
            {
                return await IncrementInventoryAsync(productId, variant.Size, variant.Color, qty);
            }

            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);

            var update = Builders<Product>.Update
                .Inc("variants.$[v].quantity", qty)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            var arrayFilters = new List<ArrayFilterDefinition>
            {
                new JsonArrayFilterDefinition<ProductVariant>("{ 'v.sku': '" + variantSku + "' }")
            };

            var updateOptions = new UpdateOptions { ArrayFilters = arrayFilters };

            var result = await _collection.UpdateOneAsync(filter, update, updateOptions);
            return result.ModifiedCount > 0;
        }

        // -----------------------
        // Pending reviews / approve / reject
        // -----------------------
        public async Task<IEnumerable<Review>> GetPendingReviewsAsync(int page = 1, int pageSize = 50)
        {
            var unwind = new BsonDocument { { "$unwind", "$reviews" } };
            var match = new BsonDocument { { "$match", new BsonDocument("reviews.approved", false) } };
            var project = new BsonDocument { { "$replaceRoot", new BsonDocument("newRoot", "$reviews") } };
            var skip = new BsonDocument { { "$skip", (page - 1) * pageSize } };
            var limit = new BsonDocument { { "$limit", pageSize } };

            var pipeline = new[] { unwind, match, project, skip, limit };

            var cursor = await _collection.AggregateAsync<Review>(pipeline);
            var list = await cursor.ToListAsync();
            return list;
        }

        public async Task<bool> ApproveReviewAsync(string productId, string reviewId)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);
            var update = Builders<Product>.Update.Set("reviews.$[r].approved", true).Set(p => p.UpdatedAt, DateTime.UtcNow);

            var options = new UpdateOptions
            {
                ArrayFilters = new List<ArrayFilterDefinition>
                {
                    new JsonArrayFilterDefinition<Review>("{ 'r.id': '" + reviewId + "' }")
                }
            };

            var res = await _collection.UpdateOneAsync(filter, update, options);
            return res.ModifiedCount > 0;
        }

        public async Task<bool> RejectReviewAsync(string productId, string reviewId)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Id, productId);
            var update = Builders<Product>.Update.PullFilter(p => p.Reviews, r => r.Id == reviewId).Set(p => p.UpdatedAt, DateTime.UtcNow);

            var res = await _collection.UpdateOneAsync(filter, update);
            return res.ModifiedCount > 0;
        }

        public async Task<bool> DecrementStockAsync(string productObjectId, string size, string currency, int quantity)
        {
            if (quantity <= 0) throw new ArgumentException("quantity must be > 0", nameof(quantity));

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productObjectId),
                Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                    pl => pl.Size == size && pl.Currency == currency && pl.Quantity >= quantity)
            );

            var update = Builders<Product>.Update
                .Inc("PriceList.$.Quantity", -quantity); // <-- update Quantity, not Stock

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task IncrementStockAsync(string productObjectId, string size, string currency, int quantity)
        {
            if (quantity <= 0) throw new ArgumentException("quantity must be > 0", nameof(quantity));

            var filter = Builders<Product>.Filter.And(
                Builders<Product>.Filter.Eq(p => p.Id, productObjectId),
                Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                    pl => pl.Size == size && pl.Currency == currency)
            );

            var update = Builders<Product>.Update
                .Inc("PriceList.$.Quantity", quantity);

            var result = await _collection.UpdateOneAsync(filter, update);
            // optionally verify ModifiedCount and log if zero (no matching price entry)
        }
    }
}
