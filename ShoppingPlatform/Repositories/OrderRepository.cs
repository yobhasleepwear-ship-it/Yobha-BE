using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.Metrics;

namespace ShoppingPlatform.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IMongoCollection<Product> _products;
        private readonly IMongoCollection<Order> _col;
        private readonly IMongoClient _mongoClient;
        private readonly IHttpClientFactory _httpClientFactory; // for delivery/payment calls
        private readonly IMongoCollection<Counter> _counters;
        private readonly string _razorpayKey; // from config
        private readonly string _blueDartUrl; // from config


        public OrderRepository(IMongoDatabase db, IMongoClient mongoClient, IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
        {
            _products = db.GetCollection<Product>("products");
            _col = db.GetCollection<Order>("orders");
            _counters = db.GetCollection<Counter>("counters");
            _mongoClient = mongoClient;
              _httpClientFactory = httpClientFactory;
        _razorpayKey = configuration["Razorpay:Key"] ?? "";
        _blueDartUrl = configuration["BlueDart:Url"] ?? "";
        }

        public async Task<IEnumerable<Order>> GetForUserAsync(string userId)
        {
            return await _col.Find(o => o.UserId == userId).SortByDescending(o => o.CreatedAt).ToListAsync();
        }

        public async Task<Order?> GetByIdAsync(string id)
        {
            return await _col.Find(o => o.Id == id).FirstOrDefaultAsync();
        }

        public async Task<Order> CreateAsync(Order order)
        {
            order.CreatedAt = DateTime.UtcNow;
            await _col.InsertOneAsync(order);
            return order;
        }

        public async Task<bool> UpdateStatusAsync(string id, string status)
        {
            var update = Builders<Order>.Update.Set(o => o.Status, status);
            var result = await _col.UpdateOneAsync(o => o.Id == id, update);
            return result.ModifiedCount > 0;
        }
        public async Task<bool> UpdateAsync(string id, Order order)
        {
            order.UpdatedAt = DateTime.UtcNow;
            var result = await _col.ReplaceOneAsync(o => o.Id == id, order);
            return result.ModifiedCount > 0;
        }

        public async Task<PagedResult<Order>> GetOrdersAdminAsync(
    int page, int pageSize, string sort, OrderFilter filter, CancellationToken ct)
        {
            var builder = Builders<Order>.Filter;
            var mongoFilter = builder.Empty;

            // 🔹 Filter by OrderId if provided
            if (!string.IsNullOrEmpty(filter.Id))
                mongoFilter &= builder.Eq(o => o.Id, filter.Id);

            // 🔹 Filter by CreatedAt date range
            if (filter.From.HasValue)
                mongoFilter &= builder.Gte(o => o.CreatedAt, filter.From.Value);

            if (filter.To.HasValue)
                mongoFilter &= builder.Lte(o => o.CreatedAt, filter.To.Value);

            // 🔹 Sorting options
            var sortDef = sort switch
            {
                "createdAt_asc" => Builders<Order>.Sort.Ascending(o => o.CreatedAt),
                "total_desc" => Builders<Order>.Sort.Descending(o => o.Total),
                _ => Builders<Order>.Sort.Descending(o => o.CreatedAt)
            };

            // 🔹 Total record count
            var totalRecords = await _col.CountDocumentsAsync(mongoFilter);

            // 🔹 Apply pagination
            var items = await _col.Find(mongoFilter)
                                 .Sort(sortDef)
                                 .Skip((page - 1) * pageSize)
                                 .Limit(pageSize)
                                 .ToListAsync(ct);

            // 🔹 Compute total pages
            var totalPages = pageSize > 0
                ? (int)Math.Ceiling((double)totalRecords / pageSize)
                : 0;

            // 🔹 Return paged result with all metadata
            return new PagedResult<Order>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = (int)totalRecords,
            };
        }

        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var filter = Builders<Order>.Filter.Eq(o => o.Id, id);
            var result = await _col.DeleteOneAsync(filter);
            return result.DeletedCount == 1;
        }
        public async Task<long> GetUserOrderCountAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return 0;

            var filter = Builders<Order>.Filter.Eq(o => o.UserId, userId);

            // Count only orders that are actually paid or confirmed
            //var completedStatuses = new[] { "Paid", "Confirmed", "Delivered" };
            //var statusFilter = Builders<Order>.Filter.In(o => o.PaymentStatus, completedStatuses);

            //var combinedFilter = Builders<Order>.Filter.And(filter, statusFilter);

            return await _col.CountDocumentsAsync(filter);
        }

        public async Task<Order> CreateOrderAsync(CreateOrderRequestV2 req, string userId)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (req.productRequests == null || !req.productRequests.Any())
                throw new ArgumentException("productRequests cannot be empty");

            // 1) fetch product docs by readable product id
            var productIds = req.productRequests.Select(p => p.id).Distinct().ToList();
            var filter = Builders<Product>.Filter.In(p => p.ProductId, productIds);
            var products = await _products.Find(filter).ToListAsync();

            if (products.Count != productIds.Count)
            {
                var missing = productIds.Except(products.Select(p => p.ProductId));
                throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");
            }

            // 2) build order items, validate price/stock
            var orderItems = new List<OrderItem>();
            foreach (var pr in req.productRequests)
            {
                var prod = products.Single(p => p.ProductId == pr.id);

                var priceEntry = prod.PriceList.FirstOrDefault(px =>
                    string.Equals(px.Size, pr.Size, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(px.Currency, req.Currency, StringComparison.OrdinalIgnoreCase));

                if (priceEntry == null)
                    throw new InvalidOperationException($"Price not found for product {pr.id}, size {pr.Size}, currency {req.Currency}");

                // prefer quantity from request if present, otherwise default 1
                int qty = 1;
                if (pr.GetType().GetProperty("Quantity") != null)
                    qty = (int?)pr.GetType().GetProperty("Quantity")!.GetValue(pr) ?? 1;
                if (qty <= 0) qty = 1;

                if (priceEntry.Quantity < qty)
                    throw new InvalidOperationException($"Insufficient stock for product {pr.id}, size {pr.Size}");

                var unitPrice = priceEntry.PriceAmount;
                var lineTotal = unitPrice * qty;

                orderItems.Add(new OrderItem
                {
                    ProductId = prod.ProductId,
                    ProductObjectId = prod.Id,
                    ProductName = prod.Name,
                    Quantity = qty,
                    Size = pr.Size,
                    UnitPrice = unitPrice,
                    LineTotal = lineTotal,
                    Currency = priceEntry.Currency,
                    ThumbnailUrl = prod.Images?.FirstOrDefault()?.ThumbnailUrl
                });
            }

            // 3) compute subtotal and apply discounts
            decimal subtotal = orderItems.Sum(i => i.LineTotal);
            decimal couponDiscount = req.CouponDiscount ?? 0m;
            decimal loyaltyDiscount = req.LoyaltyDiscountAmount ?? 0m;

            decimal discountTotal = couponDiscount + loyaltyDiscount;
            if (discountTotal > subtotal) discountTotal = subtotal;

            decimal shipping = 0m; // compute shipping as per rules
            decimal tax = 0m; // compute taxes

            decimal total = subtotal + shipping + tax - discountTotal;
            if (total < 0) total = 0m;

            // 4) assemble order document (OrderNumber will be set inside transaction)
            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                SubTotal = subtotal,
                Shipping = shipping,
                Tax = tax,
                Discount = discountTotal,
                Total = total,
                Currency = req.Currency,
                ShippingAddress = req.ShippingAddress,
                LoyaltyDiscountAmount = req.LoyaltyDiscountAmount,
                CouponCode = req.CouponCode,
                PaymentMethod = req.PaymentMethod ?? "COD",
                PaymentStatus = "Pending",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            // ---- Attempt loop: try a few times in case of extremely rare duplicate key races ----
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var session = await _mongoClient.StartSessionAsync();
                session.StartTransaction();

                try
                {
                    // determine fiscal (e.g. "2526"), and increment single ORD counter under seqs.<fiscal>
                    var fiscal = GetFiscalYearString(DateTime.UtcNow); // helper below
                    var seq = await GetNextOrderSequenceAsync(fiscal, session); // helper below (uses _counters doc with _id = "ORD")

                    // compose order number (user requested form like ORD25260000001)
                    order.OrderNumber = $"ORD{fiscal}{seq:D8}"; // D8 padding -> ORD25260000001 for seq=1

                    // extra-safety: check if an order already exists with the same OrderNumber (should be extremely rare)
                    var exists = await _col.Find(session, Builders<Order>.Filter.Eq(o => o.OrderNumber, order.OrderNumber))
                                           .FirstOrDefaultAsync();

                    if (exists != null)
                    {
                        // conflict — abort and try again (new seq next attempt)
                        await session.AbortTransactionAsync();
                        // no inventory decremented yet; just retry
                        continue;
                    }

                    // insert order (with generated OrderNumber)
                    await _col.InsertOneAsync(session, order);

                    // decrement inventory for each item using same session
                    foreach (var oi in orderItems)
                    {
                        var prodFilter = Builders<Product>.Filter.And(
                            Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId),
                            Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                                pr => pr.Size == oi.Size &&
                                      pr.Currency == oi.Currency &&
                                      pr.Quantity >= oi.Quantity)
                        );

                        var update = Builders<Product>.Update.Inc("Prices.$[elem].Quantity", -oi.Quantity)
                                                               .Inc("UpdatedAt", 0);

                        var updateOptions = new UpdateOptions
                        {
                            ArrayFilters = new List<ArrayFilterDefinition>
                    {
                        new BsonDocumentArrayFilterDefinition<BsonDocument>(
                            new BsonDocument("elem.Size", oi.Size).Add("elem.Currency", oi.Currency))
                    }
                        };

                        var result = await _products.UpdateOneAsync(session, prodFilter, update, updateOptions);

                        if (result.ModifiedCount == 0)
                        {
                            throw new InvalidOperationException($"Concurrent stock update prevented decrement for product {oi.ProductId}");
                        }
                    }

                    // persist coupon applied timestamp if applicable
                    if (!string.IsNullOrWhiteSpace(order.CouponCode))
                    {
                        order.CouponAppliedAt = DateTime.UtcNow;
                        await _col.ReplaceOneAsync(session,
                            Builders<Order>.Filter.Eq(o => o.Id, order.Id),
                            order);
                    }

                    await session.CommitTransactionAsync();

                    // success: break attempt loop and continue to payment branch below
                    break;
                }
                catch (MongoWriteException mwx) when (mwx.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // Duplicate key on insert (very rare): abort and retry another seq
                    await session.AbortTransactionAsync();
                    await TryRollbackInventory(orderItems);
                    if (attempt == maxAttempts) throw;
                    // otherwise loop to retry
                    continue;
                }
                catch (Exception)
                {
                    await session.AbortTransactionAsync();
                    await TryRollbackInventory(orderItems);
                    throw;
                }
            } // end attempts

            // 6) Payment integration branch (same as before)
            if (string.Equals(order.PaymentMethod, "razorpay", StringComparison.OrdinalIgnoreCase))
            {
                var razorOrderId = await CreateRazorpayOrder(order);
                order.RazorpayOrderId = razorOrderId;
                order.PaymentStatus = "Pending";
                await _col.ReplaceOneAsync(o => o.Id == order.Id, order);
            }

            return order;
        }


        // fiscal year helper (same as before)
        private string GetFiscalYearString(DateTime dtUtc)
        {
            int startYear = (dtUtc.Month >= 4) ? dtUtc.Year : dtUtc.Year - 1;
            var a = (startYear % 100).ToString("D2");
            var b = ((startYear + 1) % 100).ToString("D2");
            return a + b; // e.g. "2526"
        }

        // Single-counter (ORD) helper using dynamic nested field seqs.<fiscal>
        // NOTE: this uses _counters collection which must be initialized in your constructor:
        // _counters = db.GetCollection<BsonDocument>("counters");
        private async Task<long> GetNextOrderSequenceAsync(string fiscal, IClientSessionHandle? session = null)
        {
            // Counter ID pattern per fiscal year
            var counterId = $"ORD";

            var filter = Builders<Counter>.Filter.Eq(c => c.CounterFor, counterId);
            var update = Builders<Counter>.Update.Inc(c => c.Seq, 1);

            // We want the previous value before incrementing, so we can return new sequence safely
            var options = new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.Before
            };

            Counter? result;

            if (session != null)
                result = await _counters.FindOneAndUpdateAsync(session, filter, update, options);
            else
                result = await _counters.FindOneAndUpdateAsync(filter, update, options);

            long previousValue = result?.Seq ?? 0;

            // Return the new value after increment
            return previousValue + 1;
        }





        private async Task TryRollbackInventory(IEnumerable<OrderItem> items)
        {
            foreach (var oi in items)
            {
                try
                {
                    var filter = Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId);
                    var update = Builders<Product>.Update.Inc("Prices.$[elem].Quantity", oi.Quantity);
                    var updateOptions = new UpdateOptions
                    {
                        ArrayFilters = new List<ArrayFilterDefinition>
                    {
                        new BsonDocumentArrayFilterDefinition<BsonDocument>(
                            new BsonDocument("elem.Size", oi.Size).Add("elem.Currency", oi.Currency))
                    }
                    };
                    await _products.UpdateOneAsync(filter, update, updateOptions);
                }
                catch (Exception ex)
                {
                    //_logger.LogError(ex, "Rollback inventory failed for item {ProductId}", oi.ProductId);
                }
            }
        }

        private async Task<string?> CreateRazorpayOrder(Order order)
        {
            // This is a placeholder — call Razorpay Orders API here using Razorpay SDK or HTTP client.
            // Create order with amount in paise (INR * 100), currency, receipt = order.Id
            // Save returned `id` and return it.
            // Example (pseudo):
            // var request = new { amount = (int)(order.Total*100), currency = order.Currency, receipt = order.Id };
            // var client = _httpClientFactory.CreateClient("razorpay");
            // Add Basic auth header using key/secret and POST to Razorpay
            // parse response and return response.id
            await Task.CompletedTask;
            return null;
        }

        public async Task CreateShipmentWithBlueDartAsync(Order order)
        {
            // call BlueDart API only after order paid (or per your business rules)
            // build payload using order.ShippingAddress and order.Items
            var client = _httpClientFactory.CreateClient();
            var dto = new
            {
                consignor = new { /* your account details */ },
                consignee = new
                {
                    name = order.ShippingAddress?.FullName,
                    phone = order.ShippingAddress?.MobileNumner,
                    address = order.ShippingAddress?.Line1,
                    city = order.ShippingAddress?.City,
                    pincode = order.ShippingAddress?.Zip,
                },
                parcels = order.Items.Select(i => new { name = i.ProductName, quantity = i.Quantity, value = i.LineTotal }).ToList()
            };

            var json = JsonSerializer.Serialize(dto);
            var req = new HttpRequestMessage(HttpMethod.Post, _blueDartUrl + "/createShipment")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // add auth headers as BlueDart requires
            var resp = await client.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                // parse tracking id and update order
                var trackingId = ExtractTrackingFromResponse(text);
                var update = Builders<Order>.Update
                    .Set(o => o.ShippingPartner, "BlueDart")
                    .Set(o => o.ShippingTrackingId, trackingId)
                    .Set(o => o.ShippingPartnerResponse, text)
                    .Set(o => o.UpdatedAt, DateTime.UtcNow);

                await _col.UpdateOneAsync(o => o.Id == order.Id, update);
            }
            else
            {
                // log the error for manual retry
                //_logger.LogError("BlueDart create shipment failed: {Response}", text);
                // optionally persist response for debugging
                var update = Builders<Order>.Update
                    .Set(o => o.ShippingPartnerResponse, text)
                    .Set(o => o.UpdatedAt, DateTime.UtcNow);
                await _col.UpdateOneAsync(o => o.Id == order.Id, update);
            }
        }

        private string? ExtractTrackingFromResponse(string text)
        {
            // parse JSON response from BlueDart and return tracking id
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("trackingId", out var t))
                    return t.GetString();
            }
            catch { }
            return null;
        }
    }
}
