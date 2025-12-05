using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly ILogger<Product> _logger;
        public ProductRepository(IMongoDatabase database, ILogger<Product> logger, string collectionName = "products")
        {
            _collection = database.GetCollection<Product>(collectionName);
            _logger = logger;
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
            // Guard and basic vars
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var skip = (page - 1) * pageSize;
            var sortDef = sort ?? string.Empty;
            var usePriceSort = sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase)
                            || sortDef.Equals("price_desc", StringComparison.OrdinalIgnoreCase);

            var swTotalStart = Stopwatch.StartNew();
            try
            {
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
                        builder.Regex(p => p.Name, new BsonRegularExpression(qLower, "i")),
                        builder.Regex(p => p.Description, new BsonRegularExpression(qLower, "i")),
                        builder.Regex(p => p.Slug, new BsonRegularExpression(qLower, "i"))
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
                    filters.Add(builder.AnyIn(p => p.FabricType, fabric));

                // Colour - case-insensitive match on DB field 'availableColors'
                if (color != null && color.Any())
                {
                    var regexes = new BsonArray(color
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => new BsonRegularExpression(c!.Trim(), "i")));

                    var colorFilterDoc = new BsonDocument("availableColors", new BsonDocument("$in", regexes));
                    // cast BsonDocument to FilterDefinition<Product>
                    filters.Add((FilterDefinition<Product>)colorFilterDoc);
                }

                // priceElemMatch for non-aggregation find path (we won't add this to nonPriceCombined)
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
                    if (priceInner.Count > 0) priceElemMatch = Builders<Price>.Filter.And(priceInner);
                }

                var nonPriceCombined = filters.Count > 0 ? builder.And(filters) : builder.Empty;

                _logger.LogInformation("QueryAsync called. priceSort={priceSort} country={country} minPrice={minPrice} maxPrice={maxPrice} page={page} pageSize={pageSize}",
                    usePriceSort, country, minPrice, maxPrice, page, pageSize);

                if (usePriceSort)
                {
                    // ===== In-memory price-sort fallback =====
                    // 1. Fetch candidate products matching non-price filters.
                    //    We fetch a reasonable buffer (page * pageSize * multiplier)
                    //    so that sorting in memory can return correct page results.
                    int fetchMultiplier = 4; // tune: 4x pagesize is usually safe; increase if many items have same effectivePrice
                    int fetchLimit = Math.Clamp(pageSize * fetchMultiplier, pageSize, 2000); // hard cap to avoid huge payloads

                    _logger.LogInformation("Using in-memory price-sort fallback. Fetching up to {limit} candidates", fetchLimit);

                    // Query DB for non-price filtered products (projection to only fields we need could be added)
                    var candidates = await _collection.Find(nonPriceCombined)
                                                     .Limit(fetchLimit)
                                                     .ToListAsync();

                    _logger.LogInformation("Fetched {count} candidate products for in-memory sorting", candidates.Count);

                    // Compute effectivePrice for each product in-memory
                    var computed = new List<(Product product, double? effectivePrice, string debug)>();
                    foreach (var p in candidates)
                    {
                        double? eff = null;
                        string debugMsg = null;
                        try
                        {
                            // PriceList may be null
                            var priceList = p.PriceList ?? new List<Price>();

                            // Filter by country when provided, else take all entries
                            var matched = string.IsNullOrWhiteSpace(country)
                                ? priceList
                                : priceList.Where(x => !string.IsNullOrWhiteSpace(x?.Country) && x.Country.Equals(country, StringComparison.OrdinalIgnoreCase)).ToList();

                            if (matched != null && matched.Count > 0)
                            {
                                // Choose min PriceAmount among matched entries (change to First if you prefer)
                                // Handle Decimal128 / decimal / double — try Convert.ToDouble
                                double? minVal = null;
                                foreach (var pl in matched)
                                {
                                    if (pl == null) continue;
                                    try
                                    {
                                        // pl.PriceAmount may be decimal, double, int, or MongoDB.Bson.Decimal128 depending on your model
                                        object raw = pl.PriceAmount!;
                                        double dv;

                                        // try direct convert for common CLR types
                                        if (raw is decimal dec) dv = Convert.ToDouble(dec);
                                        else if (raw is double d) dv = d;
                                        else if (raw is float f) dv = Convert.ToDouble(f);
                                        else if (raw is long l) dv = Convert.ToDouble(l);
                                        else if (raw is int iv) dv = Convert.ToDouble(iv);
                                        else if (raw is MongoDB.Bson.Decimal128 d128) dv = (double)d128.ToDecimal();
                                        else
                                        {
                                            // Attempt Convert.ToDouble as last resort
                                            dv = Convert.ToDouble(raw);
                                        }

                                        if (!double.IsNaN(dv) && !double.IsInfinity(dv))
                                        {
                                            minVal = !minVal.HasValue ? dv : Math.Min(minVal.Value, dv);
                                        }
                                    }
                                    catch (Exception exConv)
                                    {
                                        // record conversion problem and continue
                                        debugMsg = $"PriceAmount conversion error for product {p.Id} priceListId={pl?.Id}: {exConv.Message}";
                                        _logger.LogWarning(debugMsg);
                                    }
                                }

                                eff = minVal;
                            }

                            // Fallback to top-level Price when no matched PriceList numeric value found
                            if (!eff.HasValue)
                            {
                                try
                                {
                                    if (p.Price != null)
                                    {
                                        object rawTop = p.Price!;
                                        double dv;
                                        if (rawTop is decimal decT) dv = Convert.ToDouble(decT);
                                        else if (rawTop is double dd) dv = dd;
                                        else if (rawTop is MongoDB.Bson.Decimal128 d128Top) dv = (double)d128Top.ToDecimal();
                                        else dv = Convert.ToDouble(rawTop);

                                        eff = dv;
                                    }
                                }
                                catch (Exception exTop)
                                {
                                    debugMsg = (debugMsg != null ? debugMsg + " | " : "") + $"Top-level Price conversion error for product {p.Id}: {exTop.Message}";
                                    _logger.LogWarning(debugMsg);
                                }
                            }
                        }
                        catch (Exception outerEx)
                        {
                            debugMsg = $"Unexpected error computing effectivePrice for product {p.Id}: {outerEx.Message}";
                            _logger.LogError(outerEx, debugMsg);
                        }

                        computed.Add((p, eff, debugMsg));
                    }

                    // Optionally log top few computed effectivePrice values for debugging
                    _logger.LogInformation("Sample effectivePrice values: {samples}",
                        string.Join(", ", computed.Take(10).Select(x => $"{x.product.Id}:{(x.effectivePrice.HasValue ? x.effectivePrice.Value.ToString("0.00") : "null")}")));

                    // Apply minPrice / maxPrice filter in-memory (if provided)
                    var filteredComputed = computed.Where(c =>
                    {
                        if (!c.effectivePrice.HasValue) return false; // hide products without any price
                        if (minPrice.HasValue && c.effectivePrice.Value < Convert.ToDouble(minPrice.Value)) return false;
                        if (maxPrice.HasValue && c.effectivePrice.Value > Convert.ToDouble(maxPrice.Value)) return false;
                        return true;
                    }).ToList();

                    // Sort by effectivePrice
                    if (sortDef.Equals("price_asc", StringComparison.OrdinalIgnoreCase))
                        filteredComputed = filteredComputed.OrderBy(c => c.effectivePrice ?? double.MaxValue).ToList();
                    else
                        filteredComputed = filteredComputed.OrderByDescending(c => c.effectivePrice ?? double.MinValue).ToList();

                    // total should reflect whole set count — we can compute total by doing a CountDocumentsAsync for non-price filters,
                    // but since we applied price filters in-memory we must compute total accordingly.
                    // If you want total = total matching non-price filters (ignoring price filter) use:
                    // long total = await _collection.CountDocumentsAsync(nonPriceCombined);
                    // But commonly total must reflect price filters — so compute total from filteredComputed (note: limited by fetchLimit).
                    // To get correct total we would need to scan entire matched set; we can't cheaply do that in-memory with fetchLimit.
                    // We'll do this: get totalNonPrice = DB count (for UI), and totalFiltered = filteredComputed.Count (from fetched candidates).
                    var totalNonPrice = await _collection.CountDocumentsAsync(nonPriceCombined);
                    var totalFilteredInCandidates = filteredComputed.Count;
                    _logger.LogInformation("totalNonPrice={totalNonPrice}, totalFilteredInCandidates={totalFilteredInCandidates}", totalNonPrice, totalFilteredInCandidates);

                    // Page the sorted results
                    var pageItems = filteredComputed.Skip(skip).Take(pageSize).Select(x => x.product).ToList();

                    // Map results
                    var mapped = pageItems.Select(p => ProductMappings.ToListItemDto(p)).ToList();

                    // NOTE: Because we only fetched 'fetchLimit' candidates, totalFilteredInCandidates may be smaller than real total.
                    // You can increase fetchMultiplier or implement a proper aggregation for accurate total+paging.
                    return (mapped, totalFilteredInCandidates);
                }
                else
                {
                    // Non-price path: build combined filter with priceElemMatch if present
                    var combinedForFindList = new List<FilterDefinition<Product>>(filters);
                    if (priceElemMatch != null) combinedForFindList.Add(builder.ElemMatch(p => p.PriceList, priceElemMatch));
                    var combinedFilterFinal = combinedForFindList.Count > 0 ? builder.And(combinedForFindList) : builder.Empty;

                    _logger.LogInformation("Executing Find with filter: {filter}", combinedFilterFinal.ToBsonDocument().ToJson());

                    try
                    {
                        var swFind = Stopwatch.StartNew();
                        var total = await _collection.CountDocumentsAsync(combinedFilterFinal);
                        swFind.Stop();
                        _logger.LogInformation("CountDocumentsAsync completed in {ms}ms, total={total}", swFind.ElapsedMilliseconds, total);

                        var sortDefBuilder = Builders<Product>.Sort;
                        SortDefinition<Product> sortDefFinal = sort switch
                        {
                            "popular" => sortDefBuilder.Descending(p => p.SalesCount),
                            "price_asc" => sortDefBuilder.Ascending(p => p.Price),
                            "price_desc" => sortDefBuilder.Descending(p => p.Price),
                            _ => sortDefBuilder.Descending(p => p.CreatedAt)
                        };

                        var swFindMain = Stopwatch.StartNew();
                        var productsCursor = await _collection.Find(combinedFilterFinal)
                            .Sort(sortDefFinal)
                            .Skip(skip)
                            .Limit(pageSize)
                            .ToListAsync();
                        swFindMain.Stop();

                        _logger.LogInformation("Find completed in {ms}ms, returned {count} products", swFindMain.ElapsedMilliseconds, productsCursor.Count);

                        var mapped = productsCursor.Select(p => ProductMappings.ToListItemDto(p)).ToList();
                        swTotalStart.Stop();
                        _logger.LogInformation("QueryAsync total time: {ms}ms", swTotalStart.ElapsedMilliseconds);
                        return (mapped, total);
                    }
                    catch (Exception findEx)
                    {
                        _logger.LogError(findEx, "Find path failed. Filter: {filter}", combinedForFindList.Count > 0 ? builder.And(combinedForFindList).ToBsonDocument().ToJson() : "{}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                swTotalStart.Stop();
                _logger.LogError(ex, "QueryAsync top-level error after {ms}ms", swTotalStart.ElapsedMilliseconds);
                throw;
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
