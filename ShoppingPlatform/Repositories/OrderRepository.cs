using MongoDB.Bson;
using MongoDB.Driver;
using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IMongoCollection<Product> _products;
        private readonly IMongoCollection<Order> _col;
        private readonly IMongoClient _mongoClient;
        private readonly IHttpClientFactory _httpClientFactory; // for delivery/payment calls
        private readonly string _razorpayKey; // from config
        private readonly string _blueDartUrl; // from config


        public OrderRepository(IMongoDatabase db, IMongoClient mongoClient, IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
        {
            _products = db.GetCollection<Product>("products");
            _col = db.GetCollection<Order>("orders");
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

            // 1) fetch product docs by readable product id (assuming Product.Id or Product.ProductId)
            var productIds = req.productRequests.Select(p => p.id).Distinct().ToList();

            var filter = Builders<Product>.Filter.In(p => p.ProductId, productIds); // change field used to query
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
                // find matching price entry
                var priceEntry = prod.PriceList.FirstOrDefault(px =>
                    string.Equals(px.Size, pr.Size, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(px.Currency, req.Currency, StringComparison.OrdinalIgnoreCase));

                if (priceEntry == null)
                    throw new InvalidOperationException($"Price not found for product {pr.id}, size {pr.Size}, currency {req.Currency}");

                // NOTE: you didn't send quantity per product in request model earlier.
                // Assuming quantity 1 for each productRequest. If quantity is required, add int Quantity in request.
                int qty = 1; // change if request includes quantity

                if (priceEntry.Quantity < qty)
                    throw new InvalidOperationException($"Insufficient stock for product {pr.id}, size {pr.Size}");

                var unitPrice = priceEntry.PriceAmount;
                var lineTotal = unitPrice * qty;

                orderItems.Add(new OrderItem
                {
                    ProductId = prod.ProductId,
                    ProductObjectId = prod.Id, // mongo _id string
                    ProductName = prod.Name,
                    Quantity = qty,
                    Size = pr.Size,
                    UnitPrice = unitPrice,
                    LineTotal = lineTotal,
                    Currency = priceEntry.Currency,
                    ThumbnailUrl = prod.Images[0].ThumbnailUrl
                });
            }

            // 3) compute subtotal and apply discounts
            decimal subtotal = orderItems.Sum(i => i.LineTotal);
            decimal couponDiscount = req.CouponDiscount ?? 0m;
            decimal loyaltyDiscount = req.LoyaltyDiscountAmount ?? 0m;

            decimal discountTotal = couponDiscount + loyaltyDiscount;
            if (discountTotal > subtotal) discountTotal = subtotal;

            decimal shipping = 0m; // compute based on rules
            decimal tax = 0m; // compute taxes

            decimal total = subtotal + shipping + tax - discountTotal;
            if (total < 0) total = 0m;

            // 4) assemble order document
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

            // 5) begin transaction: insert order and decrement inventory atomically
            using var session = await _mongoClient.StartSessionAsync();
            session.StartTransaction();

            try
            {
                // insert order
                await _col.InsertOneAsync(session, order);

                // For each order item decrement product price quantity using arrayFilters
                foreach (var oi in orderItems)
                {
                    var prodFilter = Builders<Product>.Filter.And(
                        Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId),
                        // ensure there exists a price element matching size + currency with enough qty
                        Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                            pr => pr.Size == oi.Size &&
                                  pr.Currency == oi.Currency &&
                                  pr.Quantity >= oi.Quantity)
                    );

                    var update = Builders<Product>.Update.Inc("Prices.$[elem].Quantity", -oi.Quantity)
                                                       .Inc("UpdatedAt", 0); // optional to touch

                    var arrayFilters = new[] {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                        new BsonDocument("elem.Size", oi.Size)
                        .Add("elem.Currency", oi.Currency))
                };

                    var updateOptions = new UpdateOptions
                    {
                        ArrayFilters = new List<ArrayFilterDefinition> {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                        new BsonDocument("elem.Size", oi.Size).Add("elem.Currency", oi.Currency))
                }
                    };

                    // Important: Also check quantity condition in filter to avoid negative stock
                    var result = await _products.UpdateOneAsync(session, prodFilter, update, updateOptions);

                    if (result.ModifiedCount == 0)
                    {
                        throw new InvalidOperationException($"Concurrent stock update prevented decrement for product {oi.ProductId}");
                    }
                }

                // Optionally: mark coupon usage (if you have coupon collection)
                // If coupon applied, you could write coupon usage doc here or set CouponAppliedAt
                if (!string.IsNullOrWhiteSpace(order.CouponCode))
                {
                    order.CouponAppliedAt = DateTime.UtcNow;
                    await _col.ReplaceOneAsync(session,
                        Builders<Order>.Filter.Eq(o => o.Id, order.Id),
                        order);
                }

                await session.CommitTransactionAsync();
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Error creating order, attempting rollback");
                await session.AbortTransactionAsync();
                // try rollback adjustments (if partial decrements could have occurred before failure)
                await TryRollbackInventory(orderItems);
                throw;
            }

            // 6) Payment integration branch
            if (string.Equals(order.PaymentMethod, "razorpay", StringComparison.OrdinalIgnoreCase))
            {
                // call Razorpay to create order and save RazorpayOrderId + return details
                var razorOrderId = await CreateRazorpayOrder(order);
                order.RazorpayOrderId = razorOrderId;
                order.PaymentStatus = "Pending";
                await _col.ReplaceOneAsync(o => o.Id == order.Id, order);
                // return order (with RazorpayOrderId) so frontend can proceed with payment
            }
            else
            {
                // COD: just return order
            }

            return order;
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
