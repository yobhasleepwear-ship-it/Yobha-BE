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
            string? country, // new param
            List<string>? color
        )
        {
            // guard
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var builder = Builders<Product>.Filter;
            var filters = new List<FilterDefinition<Product>>();

            // Base filters
            filters.Add(builder.Eq(p => p.IsActive, true));
            filters.Add(builder.Eq(p => p.IsDeleted, false));

            // Text search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qLower = q.Trim();
                filters.Add(builder.Or(
                    builder.Regex(p => p.Name, new MongoDB.Bson.BsonRegularExpression(qLower, "i")),
                    builder.Regex(p => p.Description, new MongoDB.Bson.BsonRegularExpression(qLower, "i")),
                    builder.Regex(p => p.Slug, new MongoDB.Bson.BsonRegularExpression(qLower, "i"))
                ));
            }

            // Category / subcategory
            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                filters.Add(builder.Or(
                    builder.Eq(p => p.ProductCategory, cat),
                    builder.Eq(p => p.ProductSubCategory, cat),
                    builder.Eq(p => p.ProductMainCategory, cat)
                ));
            }

            if (!string.IsNullOrWhiteSpace(subCategory))
            {
                var subCat = subCategory.Trim();
                filters.Add(builder.Eq(p => p.ProductCategory, subCat));
            }

            // Fabric
            if (fabric != null && fabric.Any())
            {
                filters.Add(builder.AnyIn(p => p.FabricType, fabric));
            }

            // Colour - case-insensitive match on DB field 'availableColors'
            if (color != null && color.Any())
            {
                var regexes = new MongoDB.Bson.BsonArray(
                    color
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => new MongoDB.Bson.BsonRegularExpression(c!.Trim(), "i"))
                );

                var colorFilterDoc = new MongoDB.Bson.BsonDocument("availableColors",
     new MongoDB.Bson.BsonDocument("$in", regexes));

                // Option A (cleanest)
                filters.Add((FilterDefinition<Product>)colorFilterDoc);

                // Option B (explicit)
                /// filters.Add(new MongoDB.Driver.BsonDocumentFilterDefinition<Product>(colorFilterDoc));

            }

            // Prepare non-price combined filter (used in aggregation $match and for FIND path later)
            var nonPriceCombined = filters.Count > 0 ? builder.And(filters) : builder.Empty;

            var skip = (page - 1) * pageSize;
            var sortDef = sort ?? string.Empty;
            var usePriceSort = sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase)
                            || sortDef.Equals("price_desc", StringComparison.OrdinalIgnoreCase);

            if (usePriceSort)
            {
                // ---------- AGGREGATION PIPELINE that computes effectivePrice and sorts on it ----------

                // 1) $match non-price filters
                var matchStage = new MongoDB.Bson.BsonDocument("$match", nonPriceCombined.ToBsonDocument());

                // 2) $unwind PriceList (preserve nulls so products without PriceList still appear)
                var unwindStage = new MongoDB.Bson.BsonDocument("$unwind",
                    new MongoDB.Bson.BsonDocument
                    {
                { "path", "$PriceList" },
                { "preserveNullAndEmptyArrays", true }
                    });

                // 3) If country provided, match where either PriceList.Country == country OR PriceList is null (so products without price list are kept)
                MongoDB.Bson.BsonDocument countryMatchStage = null;
                if (!string.IsNullOrWhiteSpace(country))
                {
                    countryMatchStage = new MongoDB.Bson.BsonDocument("$match", new MongoDB.Bson.BsonDocument("$or",
                        new MongoDB.Bson.BsonArray
                        {
                    new MongoDB.Bson.BsonDocument("PriceList.Country", country),
                    new MongoDB.Bson.BsonDocument("PriceList", MongoDB.Bson.BsonNull.Value)
                        }));
                }

                // 4) Convert PriceList.PriceAmount to numeric safely (use $convert with onError/onNull -> null)
                var addNumericPriceStage = new MongoDB.Bson.BsonDocument("$addFields",
                    new MongoDB.Bson.BsonDocument("numericPLPrice",
                        new MongoDB.Bson.BsonDocument("$cond", new MongoDB.Bson.BsonArray
                        {
                    // if PriceList exists and PriceAmount is numeric type then convert - else null
                    new MongoDB.Bson.BsonDocument("$in", new MongoDB.Bson.BsonArray
                    {
                        new MongoDB.Bson.BsonDocument("$type", "$PriceList.PriceAmount"),
                        new MongoDB.Bson.BsonArray { "double", "int", "long", "decimal" }
                    }),
                    new MongoDB.Bson.BsonDocument("$convert", new MongoDB.Bson.BsonDocument
                    {
                        { "input", "$PriceList.PriceAmount" },
                        { "to", "double" },
                        { "onError", MongoDB.Bson.BsonNull.Value },
                        { "onNull", MongoDB.Bson.BsonNull.Value }
                    }),
                    MongoDB.Bson.BsonNull.Value
                        })));

                // 5) Group back to product level computing min of numericPLPrice (nulls are ignored by $min)
                var groupStage = new MongoDB.Bson.BsonDocument("$group", new MongoDB.Bson.BsonDocument
        {
            { "_id", "$_id" },
            { "doc", new MongoDB.Bson.BsonDocument("$first", "$$ROOT") }, // keep full doc
            { "minPL", new MongoDB.Bson.BsonDocument("$min", "$numericPLPrice") }
        });

                // 6) Build effectivePrice = if minPL exists then minPL else convert top-level Price to double (or null)
                var addEffectivePriceStage = new MongoDB.Bson.BsonDocument("$addFields",
                    new MongoDB.Bson.BsonDocument("doc",
                        new MongoDB.Bson.BsonDocument("$mergeObjects", new MongoDB.Bson.BsonArray
                        {
                    "$doc",
                    new MongoDB.Bson.BsonDocument("effectivePrice",
                        new MongoDB.Bson.BsonDocument("$ifNull", new MongoDB.Bson.BsonArray
                        {
                            "$minPL",
                            new MongoDB.Bson.BsonDocument("$convert", new MongoDB.Bson.BsonDocument
                            {
                                { "input", "$doc.Price" },
                                { "to", "double" },
                                { "onError", MongoDB.Bson.BsonNull.Value },
                                { "onNull", MongoDB.Bson.BsonNull.Value }
                            })
                        })
                    )
                        })));

                // 7) Replace root with doc (so downstream fields are normal product fields with effectivePrice)
                var replaceRootStage = new MongoDB.Bson.BsonDocument("$replaceRoot", new MongoDB.Bson.BsonDocument("newRoot", "$doc"));

                // 8) Optionally apply minPrice/maxPrice on effectivePrice using $expr
                List<MongoDB.Bson.BsonDocument> priceExprMatches = new List<MongoDB.Bson.BsonDocument>();
                if (minPrice.HasValue)
                    priceExprMatches.Add(new MongoDB.Bson.BsonDocument("$gte", new MongoDB.Bson.BsonArray { "$effectivePrice", Convert.ToDouble(minPrice.Value) }));
                if (maxPrice.HasValue)
                    priceExprMatches.Add(new MongoDB.Bson.BsonDocument("$lte", new MongoDB.Bson.BsonArray { "$effectivePrice", Convert.ToDouble(maxPrice.Value) }));

                MongoDB.Bson.BsonDocument priceFilterMatchStage = null;
                if (priceExprMatches.Count > 0)
                {
                    priceFilterMatchStage = new MongoDB.Bson.BsonDocument("$match", new MongoDB.Bson.BsonDocument("$expr",
                        new MongoDB.Bson.BsonDocument("$and", new MongoDB.Bson.BsonArray(priceExprMatches))));
                }

                // 9) Sort, skip, limit stages
                var sortDirection = sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                var sortStage = new MongoDB.Bson.BsonDocument("$sort", new MongoDB.Bson.BsonDocument
        {
            { "effectivePrice", sortDirection },
            { "CreatedAt", -1 }
        });

                var skipStage = new MongoDB.Bson.BsonDocument("$skip", skip);
                var limitStage = new MongoDB.Bson.BsonDocument("$limit", pageSize);

                // Build full pipeline list
                var pipeline = new List<MongoDB.Bson.BsonDocument> { matchStage, unwindStage };
                if (countryMatchStage != null) pipeline.Add(countryMatchStage);
                pipeline.Add(addNumericPriceStage);
                pipeline.Add(groupStage);
                pipeline.Add(addEffectivePriceStage);
                if (priceFilterMatchStage != null) pipeline.Add(priceFilterMatchStage);
                pipeline.Add(replaceRootStage);
                pipeline.Add(sortStage);

                // Build count pipeline (same as above but with $count, without skip/limit)
                var countPipeline = new List<MongoDB.Bson.BsonDocument>(pipeline);
                countPipeline.Add(new MongoDB.Bson.BsonDocument("$count", "count"));

                // Execute count aggregation
                var countAgg = _collection.Aggregate().AppendStage<MongoDB.Bson.BsonDocument>(countPipeline[0]);
                for (int i = 1; i < countPipeline.Count; i++) countAgg = countAgg.AppendStage<MongoDB.Bson.BsonDocument>(countPipeline[i]);

                var countResult = await countAgg.ToListAsync();
                long total = 0;
                if (countResult != null && countResult.Count > 0)
                    total = countResult[0].GetValue("count").ToInt64();

                // Add paging to pipeline and execute main aggregation
                pipeline.Add(skipStage);
                pipeline.Add(limitStage);

                var agg = _collection.Aggregate().AppendStage<Product>(pipeline[0]);
                for (int i = 1; i < pipeline.Count; i++) agg = agg.AppendStage<Product>(pipeline[i]);

                var productsCursor = await agg.ToListAsync();
                var mapped = productsCursor.Select(p => ProductMappings.ToListItemDto(p)).ToList();
                return (mapped, total);
            }
            else
            {
                // ---------- Non-price sort path (FIND) ----------
                // If min/max/country price filters were requested, add an ElemMatch on PriceList for the find path
                FilterDefinition<Price>? priceElemMatch = null;
                if (minPrice.HasValue || maxPrice.HasValue || !string.IsNullOrWhiteSpace(country))
                {
                    var priceInner = new List<FilterDefinition<Price>>();
                    if (!string.IsNullOrWhiteSpace(country))
                        priceInner.Add(Builders<Price>.Filter.Eq(p => p.Country, country));
                    if (minPrice.HasValue)
                        priceInner.Add(Builders<Price>.Filter.Gte(p => p.PriceAmount, minPrice.Value));
                    if (maxPrice.HasValue)
                        priceInner.Add(Builders<Price>.Filter.Lte(p => p.PriceAmount, maxPrice.Value));
                    if (priceInner.Count > 0)
                        priceElemMatch = Builders<Price>.Filter.And(priceInner);
                }

                var combinedForFindList = new List<FilterDefinition<Product>> { nonPriceCombined };
                if (priceElemMatch != null)
                {
                    combinedForFindList.Add(builder.ElemMatch(p => p.PriceList, priceElemMatch));
                }

                var combinedFilterFinal = combinedForFindList.Count > 0 ? builder.And(combinedForFindList) : builder.Empty;

                var total = await _collection.CountDocumentsAsync(combinedFilterFinal);

                var sortDefBuilder = Builders<Product>.Sort;
                SortDefinition<Product> sortDefFinal = sort switch
                {
                    "popular" => sortDefBuilder.Descending(p => p.SalesCount),
                    "price_asc" => sortDefBuilder.Ascending(p => p.Price),
                    "price_desc" => sortDefBuilder.Descending(p => p.Price),
                    _ => sortDefBuilder.Descending(p => p.CreatedAt)
                };

                var productsCursor = await _collection.Find(combinedFilterFinal)
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
