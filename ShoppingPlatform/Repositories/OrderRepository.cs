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
using ShoppingPlatform.Helpers;

namespace ShoppingPlatform.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IMongoCollection<Product> _products;
        private readonly IMongoCollection<Order> _col;
        private readonly IMongoClient _mongoClient;
        private readonly IHttpClientFactory _httpClientFactory; // for delivery/payment calls
        private readonly IMongoCollection<Counter> _counters;
        private readonly GiftCardHelper _giftCardHelper;
        private readonly PaymentHelper _paymentHelper;
        //private readonly IMongoCollection<GiftCard> _giftCardCollection;


        public OrderRepository(IMongoDatabase db, IMongoClient mongoClient, IHttpClientFactory httpClientFactory,
        IConfiguration configuration,GiftCardHelper giftCardHelper,
                        PaymentHelper paymentHelper
                        //,IMongoCollection<GiftCard> giftCardCollection
            )
        {
            _products = db.GetCollection<Product>("products");
            _col = db.GetCollection<Order>("orders");
            _counters = db.GetCollection<Counter>("counters");
            _mongoClient = mongoClient;
              _httpClientFactory = httpClientFactory;
            _giftCardHelper = giftCardHelper;
            _paymentHelper = paymentHelper;
            //_giftCardCollection = giftCardCollection;
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
            if (req.productRequests == null) req.productRequests = new List<ProductRequest>();

            // Basic validation
            if (string.IsNullOrWhiteSpace(req.Currency)) throw new ArgumentException("Currency required");
            if (req.ShippingAddress == null && (req.productRequests == null || !req.productRequests.Any()))
            {
                // allow empty shipping address only if buying gift card
                if (req.GiftCardAmount == null) throw new ArgumentException("ShippingAddress required for non-gift-card orders");
            }

            // resolve products only if there are items
            var orderItems = new List<OrderItem>();
            List<Product> products = new List<Product>();
            if (req.productRequests.Any())
            {
                var productIds = req.productRequests.Select(p => p.id).Distinct().ToList();
                var filter = Builders<Product>.Filter.In(p => p.ProductId, productIds);
                products = await _products.Find(filter).ToListAsync();

                if (products.Count != productIds.Count)
                {
                    var missing = productIds.Except(products.Select(p => p.ProductId));
                    throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");
                }

                foreach (var pr in req.productRequests)
                {
                    var prod = products.Single(p => p.ProductId == p.Id);

                    var priceEntry = prod.PriceList.FirstOrDefault(px =>
                        string.Equals(px.Size, pr.Size, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(px.Currency, req.Currency, StringComparison.OrdinalIgnoreCase));

                    if (priceEntry == null) throw new InvalidOperationException($"Price not found for product {pr.id}, size {pr.Size}, currency {req.Currency}");

                    int qty = pr.Quantity > 0 ? pr.Quantity : 1;
                    if (priceEntry.Quantity < qty) throw new InvalidOperationException($"Insufficient stock for product {pr.id}");

                    decimal unitPrice = priceEntry.PriceAmount;
                    decimal lineTotal = unitPrice * qty;

                    orderItems.Add(new OrderItem
                    {
                        ProductId = prod.ProductId,
                        ProductObjectId = prod.Id,
                        ProductName = prod.Name,
                        Quantity = qty,
                        Size = pr.Size,
                        Fabric = pr.Fabric,
                        Color = pr.Color,
                        UnitPrice = unitPrice,
                        LineTotal = lineTotal,
                        Currency = priceEntry.Currency,
                        ThumbnailUrl = prod.Images?.FirstOrDefault()?.ThumbnailUrl,
                        Monogram = pr.Monogram
                    });
                }
            }

            // compute totals
            decimal subtotal = orderItems.Sum(i => i.LineTotal);
            decimal couponDiscount = req.CouponDiscount ?? 0m;
            decimal loyaltyDiscount = req.LoyaltyDiscountAmount ?? 0m;
            decimal discountTotal = couponDiscount + loyaltyDiscount;

            if (discountTotal > subtotal) discountTotal = subtotal;

            decimal shipping = 0m; // compute shipping rules
            decimal tax = 0m;
            decimal totalBeforeGiftCard = subtotal + shipping + tax - discountTotal;
            if (totalBeforeGiftCard < 0) totalBeforeGiftCard = 0m;

            // assemble basic order (OrderNumber and Id set inside transaction)
            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                SubTotal = subtotal,
                Shipping = shipping,
                Tax = tax,
                Discount = discountTotal,
                Total = totalBeforeGiftCard, // might change if gift card applied
                Currency = req.Currency,
                ShippingAddress = req.ShippingAddress,
                LoyaltyDiscountAmount = req.LoyaltyDiscountAmount,
                CouponCode = req.CouponCode,
                PaymentMethod = req.PaymentMethod ?? "COD",
                PaymentStatus = "Pending",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                GiftCardNumber = req.GiftCardNumber,
                GiftCardAmount = req.GiftCardAmount,
                ShippingRemarks = req.ShippingRemarks,
            };

            // Special flow: BUYING a gift card (no items, but giftCardAmount present and no giftCardNumber)
            if (!order.Items.Any() && order.GiftCardAmount.HasValue && string.IsNullOrWhiteSpace(order.GiftCardNumber))
            {  
                using var session = await _mongoClient.StartSessionAsync();
                session.StartTransaction();
                try
                {
                    var fiscal = GetFiscalYearString(DateTime.UtcNow);
                    var seq = await GetNextOrderSequenceAsync(fiscal, session);
                    order.OrderNumber = $"ORD{fiscal}{seq:D8}";

                    await _col.InsertOneAsync(session, order);

                    var giftCard = await _giftCardHelper.CreateGiftCardAsync(session, order.GiftCardAmount.Value, order.Currency, order.Id, userId);

                    var update = Builders<Order>.Update
                        .Set(o => o.GiftCardId, giftCard.Id)
                        .Set(o => o.GiftCardNumber, giftCard.GiftCardNumber);
                    await _col.UpdateOneAsync(session, Builders<Order>.Filter.Eq(o => o.Id, order.Id), update);

                    await session.CommitTransactionAsync();

                    order.GiftCardId = giftCard.Id;
                    order.GiftCardNumber = giftCard.GiftCardNumber;
                    return order;
                }
                catch (Exception)
                {
                    await session.AbortTransactionAsync();
                    throw;
                }
            }

            // Common path
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var session = await _mongoClient.StartSessionAsync();
                session.StartTransaction();
                try
                {
                    var fiscal = GetFiscalYearString(DateTime.UtcNow);
                    var seq = await GetNextOrderSequenceAsync(fiscal, session);
                    order.OrderNumber = $"ORD{fiscal}{seq:D8}";

                    await _col.InsertOneAsync(session, order);

                    foreach (var oi in orderItems)
                    {
                        var prodFilter = Builders<Product>.Filter.And(
                            Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId),
                            Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                                pr => pr.Size == oi.Size && pr.Currency == oi.Currency && pr.Quantity >= oi.Quantity)
                        );

                        var update = Builders<Product>.Update
                            .Inc($"{"PriceList"}.$[elem].Quantity", -oi.Quantity)
                            .Set(p => p.UpdatedAt, DateTime.UtcNow);

                        var updateOptions = new UpdateOptions
                        {
                            ArrayFilters = new List<ArrayFilterDefinition>
                    {
                        new BsonDocumentArrayFilterDefinition<BsonDocument>(
                            new BsonDocument { { "elem.Size", oi.Size }, { "elem.Currency", oi.Currency } })
                    }
                        };

                        var result = await _products.UpdateOneAsync(session, prodFilter, update, updateOptions);
                        if (result.ModifiedCount == 0)
                        {
                            await session.AbortTransactionAsync();
                            throw new InvalidOperationException($"Concurrent stock update prevented decrement for product {oi.ProductId}");
                        }
                    }

                    // Apply gift card if provided
                    if (!string.IsNullOrWhiteSpace(req.GiftCardNumber))
                    {
                        var currentTotal = order.Total;
                        if (currentTotal > 0)
                        {
                            var deductionResult = await _giftCardHelper.ApplyGiftCardAsync(session, req.GiftCardNumber.Trim(), currentTotal, order.Currency);

                            var orderUpdate = Builders<Order>.Update
                                .Set(o => o.GiftCardNumber, req.GiftCardNumber.Trim())
                                .Set(o => o.GiftCardAppliedAmount, deductionResult.Deducted)
                                .Set(o => o.GiftCardId, deductionResult.GiftCardId)
                                .Set(o => o.GiftCardAppliedAt, DateTime.UtcNow)
                                .Inc(o => o.Total, -deductionResult.Deducted);

                            await _col.UpdateOneAsync(session, Builders<Order>.Filter.Eq(o => o.Id, order.Id), orderUpdate);

                            order.GiftCardAppliedAmount = deductionResult.Deducted;
                            order.GiftCardId = deductionResult.GiftCardId;
                            order.GiftCardNumber = req.GiftCardNumber.Trim();
                            order.GiftCardAppliedAt = DateTime.UtcNow;
                            order.Total -= deductionResult.Deducted;
                            if (order.Total < 0) order.Total = 0m;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(order.CouponCode))
                    {
                        var couponUpdate = Builders<Order>.Update
                            .Set(o => o.CouponAppliedAt, DateTime.UtcNow);
                        await _col.UpdateOneAsync(session, Builders<Order>.Filter.Eq(o => o.Id, order.Id), couponUpdate);
                    }

                    await session.CommitTransactionAsync();

                    if (string.Equals(order.PaymentMethod, "razorpay", StringComparison.OrdinalIgnoreCase))
                    {
                        bool isInternational = !string.Equals(order.Currency, "INR", StringComparison.OrdinalIgnoreCase);

                        var razorOrderId = await _paymentHelper.CreateRazorpayOrderAsync(order.OrderNumber, order.Total, order.Currency, isInternational);

                        var payUpdate = Builders<Order>.Update
                            .Set(o => o.RazorpayOrderId, razorOrderId)
                            .Set(o => o.PaymentStatus, "Pending");

                        await _col.UpdateOneAsync(o => o.Id == order.Id, payUpdate);
                        order.RazorpayOrderId = razorOrderId;
                        order.PaymentStatus = "Pending";
                    }

                    return order;
                }
                catch (MongoWriteException mwx) when (mwx.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    await session.AbortTransactionAsync();
                    if (attempt == maxAttempts) throw;
                    continue;
                }
                catch (Exception)
                {
                    await session.AbortTransactionAsync();
                    await TryRollbackInventoryAsync(orderItems);
                    throw;
                }
            }

            throw new InvalidOperationException("Failed to create order after retries.");
        }
        // helper: rollback inventory if needed (best-effort compensating update)
        private async Task TryRollbackInventoryAsync(IEnumerable<OrderItem> orderItems)
        {
            // Attempt to restore stock levels if a transaction failed after partial updates
            foreach (var oi in orderItems)
            {
                try
                {
                    var prodFilter = Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId);

                    var update = Builders<Product>.Update
                        .Inc("PriceList.$[elem].Quantity", oi.Quantity)
                        .Set(p => p.UpdatedAt, DateTime.UtcNow);

                    var updateOptions = new UpdateOptions
                    {
                        ArrayFilters = new List<ArrayFilterDefinition>
                {
                    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                        new BsonDocument
                        {
                            { "elem.Size", oi.Size },
                            { "elem.Currency", oi.Currency }
                        })
                }
                    };

                    var result = await _products.UpdateOneAsync(prodFilter, update, updateOptions);

                    if (result.ModifiedCount == 0)
                        throw new InvalidOperationException($"Rollback failed — product {oi.ProductObjectId} not updated (Size={oi.Size}, Currency={oi.Currency}).");
                }
                catch (Exception ex)
                {
                    // Re-throw detailed rollback error to bubble up to upper layers
                    throw new Exception($"Rollback inventory failed for product {oi.ProductObjectId}", ex);
                }
            }
        }


        // stub: produce fiscal string "2526" for FY 2025-26 for example
        private string GetFiscalYearString(DateTime dt)
        {
            // Implement your fiscal logic here (example: FY starting Apr 1)
            var year = dt.Month >= 4 ? dt.Year % 100 : (dt.Year - 1) % 100;
            var next = (year + 1) % 100;
            // e.g. year=25,next=26 => "2526"
            return $"{year:D2}{next:D2}";
        }

        // stub: increment sequence doc in _counters collection atomically; return sequence number
        private async Task<long> GetNextOrderSequenceAsync(string fiscal, IClientSessionHandle session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var key = $"ORD_{fiscal}";

            // typed filter & update
            var filter = Builders<Counter>.Filter.Eq(c => c.Id, key);
            var update = Builders<Counter>.Update.Inc(c => c.Seq, 1);

            var options = new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var doc = await _counters.FindOneAndUpdateAsync(session, filter, update, options);
            if (doc == null)
            {
                // This should not happen when upsert = true, but guard just in case
                throw new InvalidOperationException($"Failed to obtain sequence for {key}");
            }

            return doc.Seq;
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

        //public async Task CreateShipmentWithBlueDartAsync(Order order)
        //{
        //    // call BlueDart API only after order paid (or per your business rules)
        //    // build payload using order.ShippingAddress and order.Items
        //    var client = _httpClientFactory.CreateClient();
        //    var dto = new
        //    {
        //        consignor = new { /* your account details */ },
        //        consignee = new
        //        {
        //            name = order.ShippingAddress?.FullName,
        //            phone = order.ShippingAddress?.MobileNumner,
        //            address = order.ShippingAddress?.Line1,
        //            city = order.ShippingAddress?.City,
        //            pincode = order.ShippingAddress?.Zip,
        //        },
        //        parcels = order.Items.Select(i => new { name = i.ProductName, quantity = i.Quantity, value = i.LineTotal }).ToList()
        //    };

        //    var json = JsonSerializer.Serialize(dto);
        //    var req = new HttpRequestMessage(HttpMethod.Post, _blueDartUrl + "/createShipment")
        //    {
        //        Content = new StringContent(json, Encoding.UTF8, "application/json")
        //    };

        //    // add auth headers as BlueDart requires
        //    var resp = await client.SendAsync(req);
        //    var text = await resp.Content.ReadAsStringAsync();

        //    if (resp.IsSuccessStatusCode)
        //    {
        //        // parse tracking id and update order
        //        var trackingId = ExtractTrackingFromResponse(text);
        //        var update = Builders<Order>.Update
        //            .Set(o => o.ShippingPartner, "BlueDart")
        //            .Set(o => o.ShippingTrackingId, trackingId)
        //            .Set(o => o.ShippingPartnerResponse, text)
        //            .Set(o => o.UpdatedAt, DateTime.UtcNow);

        //        await _col.UpdateOneAsync(o => o.Id == order.Id, update);
        //    }
        //    else
        //    {
        //        // log the error for manual retry
        //        //_logger.LogError("BlueDart create shipment failed: {Response}", text);
        //        // optionally persist response for debugging
        //        var update = Builders<Order>.Update
        //            .Set(o => o.ShippingPartnerResponse, text)
        //            .Set(o => o.UpdatedAt, DateTime.UtcNow);
        //        await _col.UpdateOneAsync(o => o.Id == order.Id, update);
        //    }
        //}

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
