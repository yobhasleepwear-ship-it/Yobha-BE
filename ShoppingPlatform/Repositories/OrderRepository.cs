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
using Xunit.Sdk;
using System.Text.RegularExpressions;
using ShoppingPlatform.Services;
using DocumentFormat.OpenXml.Drawing.Charts;
using Order = ShoppingPlatform.Models.Order;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Office2010.Excel;

namespace ShoppingPlatform.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IMongoCollection<Product> _products;
        private readonly IMongoCollection<CouponUsage> _couponUsages;
        private readonly IMongoCollection<Coupon> _coupons;
        private readonly IMongoCollection<Order> _col;
        private readonly IMongoClient _mongoClient;
        private readonly IHttpClientFactory _httpClientFactory; // for delivery/payment calls
        private readonly IMongoCollection<Counter> _counters;
        private readonly GiftCardHelper _giftCardHelper;
        private readonly PaymentHelper _paymentHelper;
        //private readonly IMongoCollection<GiftCard> _giftCardCollection;
        UserRepository _userRepository;
        private readonly ILoyaltyPointAuditService _loyaltyPointAuditService;
        private readonly ISmsGatewayService _smsGatewayService;


        public OrderRepository(IMongoDatabase db, IMongoClient mongoClient, IHttpClientFactory httpClientFactory,
        IConfiguration configuration,GiftCardHelper giftCardHelper,
                        PaymentHelper paymentHelper
                        //,IMongoCollection<GiftCard> giftCardCollection
                        ,UserRepository userRepository,ILoyaltyPointAuditService loyaltyPointAuditService, ISmsGatewayService smsGatewayService
            )
        {
            _products = db.GetCollection<Product>("products");
            _col = db.GetCollection<Order>("orders");
            _counters = db.GetCollection<Counter>("counters");
            _couponUsages = db.GetCollection<CouponUsage>("couponUsages");
            _coupons = db.GetCollection<Coupon>("coupons");
            _mongoClient = mongoClient;
              _httpClientFactory = httpClientFactory;
            _giftCardHelper = giftCardHelper;
            _paymentHelper = paymentHelper;
            //_giftCardCollection = giftCardCollection;
            _userRepository = userRepository;
            _loyaltyPointAuditService = loyaltyPointAuditService;
            _smsGatewayService = smsGatewayService;
        }

        public async Task<IEnumerable<Order>> GetForUserAsync(string userId)
        {
            var builder = Builders<Order>.Filter;

            // ✅ Base filter: orders for user
            var mongoFilter = builder.Eq(o => o.UserId, userId);

            // ❌ Exclude Razorpay orders without RazorpayPaymentId
            mongoFilter &= builder.Not(
                builder.And(
                    builder.Eq(o => o.PaymentMethod, "razorpay"),
                    builder.Or(
                        builder.Eq(o => o.RazorpayPaymentId, null),
                        builder.Eq(o => o.RazorpayPaymentId, "")
                    )
                )
            );

            return await _col
                .Find(mongoFilter)
                .SortByDescending(o => o.CreatedAt)
                .ToListAsync();
        }


        public async Task<Order?> GetByIdAsync(string id)
        {
            return await _col.Find(o => o.Id == id).FirstOrDefaultAsync();
        }

        public async Task<Order?> GetByOrderNumberAsync(string OrderNumber)
        {
            return await _col.Find(o => o.OrderNumber == OrderNumber).FirstOrDefaultAsync();
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

        //    public async Task<PagedResult<Order>>    GetOrdersAdminAsync(
        //int page, int pageSize, string sort, OrderFilter filter, CancellationToken ct)
        //    {
        //        var builder = Builders<Order>.Filter;
        //        var mongoFilter = builder.Empty;

        //        // 🔹 Filter by OrderId if provided
        //        if (!string.IsNullOrEmpty(filter.Id))
        //            mongoFilter &= builder.Eq(o => o.Id, filter.Id);

        //        // 🔹 Filter by CreatedAt date range
        //        if (filter.From.HasValue)
        //            mongoFilter &= builder.Gte(o => o.CreatedAt, filter.From.Value);

        //        if (filter.To.HasValue)
        //            mongoFilter &= builder.Lte(o => o.CreatedAt, filter.To.Value);

        //        // 🔹 Sorting options
        //        var sortDef = sort switch
        //        {
        //            "createdAt_asc" => Builders<Order>.Sort.Ascending(o => o.CreatedAt),
        //            "total_desc" => Builders<Order>.Sort.Descending(o => o.Total),
        //            _ => Builders<Order>.Sort.Descending(o => o.CreatedAt)
        //        };

        //        // 🔹 Total record count
        //        var totalRecords = await _col.CountDocumentsAsync(mongoFilter);

        //        // 🔹 Apply pagination
        //        var items = await _col.Find(mongoFilter)
        //                             .Sort(sortDef)
        //                             .Skip((page - 1) * pageSize)
        //                             .Limit(pageSize)
        //                             .ToListAsync(ct);

        //        // 🔹 Compute total pages
        //        var totalPages = pageSize > 0
        //            ? (int)Math.Ceiling((double)totalRecords / pageSize)
        //            : 0;

        //        // 🔹 Return paged result with all metadata
        //        return new PagedResult<Order>
        //        {
        //            Items = items,
        //            Page = page,
        //            PageSize = pageSize,
        //            TotalCount = (int)totalRecords,
        //        };
        //    }

        public async Task<PagedResult<Order>> GetOrdersAdminAsync(
    int page,
    int pageSize,
    string sort,
    OrderFilter filter,
    CancellationToken ct)
        {
            var builder = Builders<Order>.Filter;
            var mongoFilter = builder.Empty;

            // 🔹 Filter by OrderId
            if (!string.IsNullOrEmpty(filter.Id))
                mongoFilter &= builder.Eq(o => o.Id, filter.Id);

            // 🔹 Date range filter
            if (filter.From.HasValue)
                mongoFilter &= builder.Gte(o => o.CreatedAt, filter.From.Value);

            if (filter.To.HasValue)
                mongoFilter &= builder.Lte(o => o.CreatedAt, filter.To.Value);

            // ❌ Exclude Razorpay orders without RazorpayOrderId
            mongoFilter &= builder.Not(
                builder.And(
                    builder.Eq(o => o.PaymentMethod, "razorpay"),
                    builder.Or(
                        builder.Eq(o => o.RazorpayPaymentId, null),
                        builder.Eq(o => o.RazorpayPaymentId, "")
                    )
                )
            );

            // 🔹 Sorting
            var sortDef = sort switch
            {
                "createdAt_asc" => Builders<Order>.Sort.Ascending(o => o.CreatedAt),
                "total_desc" => Builders<Order>.Sort.Descending(o => o.Total),
                _ => Builders<Order>.Sort.Descending(o => o.CreatedAt)
            };

            var totalRecords = await _col.CountDocumentsAsync(mongoFilter, cancellationToken: ct);

            var items = await _col.Find(mongoFilter)
                .Sort(sortDef)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(ct);

            return new PagedResult<Order>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = (int)totalRecords
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

        public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequestV2 req, string userId)
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
            var user = await _userRepository.GetByIdAsync(userId);
            // resolve products only if there are items
            var orderItems = new List<OrderItem>();
            List<Product> products = new List<Product>();
            if (req.productRequests.Any())
            {
                var productIds = req.productRequests.Select(p => p.id).Distinct().ToList();
                var filter = Builders<Product>.Filter.In(p => p.Id, productIds);
                products = await _products.Find(filter).ToListAsync();

                if (products.Count != productIds.Count)
                {
                    var missing = productIds.Except(products.Select(p => p.ProductId));
                    throw new InvalidOperationException($"Products not found: {string.Join(',', missing)}");
                }

                foreach (var pr in req.productRequests)
                {
                    var prod = products.Where(p => p.Id == pr.id).FirstOrDefault();

                    var priceEntry = prod.PriceList.FirstOrDefault(px =>
                        string.Equals(px.Size, pr.Size, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(px.Currency, req.Currency, StringComparison.OrdinalIgnoreCase));

                    if (priceEntry == null) throw new InvalidOperationException($"Price not found for product {pr.id}, size {pr.Size}, currency {req.Currency}");

                    int qty = pr.Quantity > 0 ? pr.Quantity : 1;
                    //if (priceEntry.Quantity < qty) throw new InvalidOperationException($"Insufficient stock for product {pr.id}");

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
            decimal Total = subtotal + shipping + tax - discountTotal;

            if ((req.GiftCardAmount ?? 0m) > 0m && string.IsNullOrEmpty(req.GiftCardNumber))
            {
                Total += (req.GiftCardAmount ?? 0m);
            }
            if (req.isGiftWrap == true)
            {
                Total += 500m;
            }
            if (req.shippingPrice != null && req.shippingPrice >= 0m)
            {
                Total += (req.shippingPrice ?? 0m);
            }

            // assemble basic order (OrderNumber and Id set inside transaction)
            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                SubTotal = subtotal,
                Shipping = shipping,
                Tax = tax,
                Discount = discountTotal,
                Total = Total, // might change if gift card applied
                Currency = req.Currency,
                ShippingAddress = req.ShippingAddress,
                LoyaltyDiscountAmount = req.LoyaltyDiscountAmount,
                CouponCode = req.CouponCode,
                PaymentMethod = req.PaymentMethod ?? "COD",
                PaymentStatus = "Pending",
                Status = req.PaymentMethod?.ToUpper() == "COD"? "Confirmed":"Pending",
                CreatedAt = DateTime.UtcNow,
                GiftCardNumber = req.GiftCardNumber,
                GiftCardAmount = req.GiftCardAmount,
                ShippingRemarks = req.ShippingRemarks,
                orderCountry = req.orderCountry,
                Email = req.Email,
                isGiftWrap = req.isGiftWrap,
                delhiveryShipment = req.delhiveryShipment,
                shippingPrice = req.shippingPrice,
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

                    // --- NEW: If payment method is razorpay, create Razorpay order and persist debug info ---
                    if (string.Equals(order.PaymentMethod, "razorpay", StringComparison.OrdinalIgnoreCase))
                    {
                        bool isInternational = !string.Equals(order.Currency, "INR", StringComparison.OrdinalIgnoreCase);

                        var razorResult = await _paymentHelper.CreateRazorpayOrderAsync(order.OrderNumber, order.Total, order.Currency, isInternational);

                        var gatewayDebug = new
                        {
                            Request = razorResult.RequestPayload,
                            Response = razorResult.ResponseBody,
                            StatusCode = razorResult.StatusCode,
                            Error = razorResult.ErrorMessage
                        };
                        string gatewayDebugJson = JsonSerializer.Serialize(gatewayDebug);

                        var payUpdate = Builders<Order>.Update
                            .Set(o => o.PaymentGatewayResponse, gatewayDebugJson)
                            .Set(o => o.PaymentStatus, razorResult.Success ? "Pending" : "Failed");

                        if (razorResult.Success && !string.IsNullOrWhiteSpace(razorResult.RazorpayOrderId))
                        {
                            payUpdate = payUpdate.Set(o => o.RazorpayOrderId, razorResult.RazorpayOrderId);
                            order.RazorpayOrderId = razorResult.RazorpayOrderId;
                            order.PaymentStatus = "Pending";
                        }
                        else
                        {
                            order.PaymentStatus = "Failed";
                        }

                        // persist payment fields (use session overload to be consistent)
                        await _col.UpdateOneAsync(session, Builders<Order>.Filter.Eq(o => o.Id, order.Id), payUpdate);

                        return new CreateOrderResponse
                        {
                            Success = razorResult.Success,
                            Id = order.Id?.ToString() ?? string.Empty,
                            OrderId = order.OrderNumber ?? order.Id?.ToString() ?? string.Empty,
                            RazorpayOrderId = razorResult.RazorpayOrderId,
                            Total = order.Total,
                            GiftCardNumber = order.GiftCardNumber,
                        };
                    }

                    var smsuser = _smsGatewayService.SendOrderConfirmationSmsAsync(order.ShippingAddress.MobileNumner, user?.FullName?? order.ShippingAddress.FullName, order.GiftCardNumber, order.GiftCardAmount+"");
                    var smsadmin = _smsGatewayService.SendAdminNewOrderSmsAsync(order.ShippingAddress.MobileNumner, user?.FullName ?? order.ShippingAddress.FullName, order.GiftCardNumber, order.GiftCardAmount + "");
                    // Non-razorpay payment for gift card: return as before
                    return new CreateOrderResponse
                    {
                        Success = true,
                        Id = order.Id?.ToString() ?? string.Empty,
                        OrderId = order.OrderNumber ?? order.Id?.ToString() ?? string.Empty,
                        RazorpayOrderId = order.RazorpayOrderId,
                        Total = order.Total,
                        GiftCardNumber = order.GiftCardNumber,
                    };
                }
                catch (Exception)
                {
                    try { await session.AbortTransactionAsync(); } catch { /* ignore if already aborted */ }
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

                    if (req.LoyaltyDiscountAmount != null && req.LoyaltyDiscountAmount > 0)
                    {
                        var pts = await _userRepository.TryDeductLoyaltyPointsAsync(order.UserId, (req.LoyaltyDiscountAmount ?? 0m));
                        var updateloyalty = _loyaltyPointAuditService.RecordSimpleAsync(order.UserId, "Debit", req.LoyaltyDiscountAmount ?? 0m, "Order Created", null, order.Email, null, pts);

                    }

                    //foreach (var oi in orderItems)
                    //{
                    //    var prodFilter = Builders<Product>.Filter.And(
                    //        Builders<Product>.Filter.Eq(p => p.Id, oi.ProductObjectId),
                    //        Builders<Product>.Filter.ElemMatch(p => p.PriceList,
                    //            pr => pr.Size == oi.Size && pr.Currency == oi.Currency && pr.Quantity >= oi.Quantity)
                    //    );

                    //    var update = Builders<Product>.Update
                    //        .Inc($"{"PriceList"}.$[elem].Quantity", -oi.Quantity)
                    //        .Set(p => p.UpdatedAt, DateTime.UtcNow);

                    //    var updateOptions = new UpdateOptions
                    //    {
                    //        ArrayFilters = new List<ArrayFilterDefinition>
                    //{
                    //    new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    //        new BsonDocument { { "elem.Size", oi.Size }, { "elem.Currency", oi.Currency } })
                    //}
                    //    };

                    //    //var result = await _products.UpdateOneAsync(session, prodFilter, update, updateOptions);
                    //    if (result.ModifiedCount == 0)
                    //    {
                    //        // DO NOT abort here - throw and let outer catch handle the abort.
                    //        throw new InvalidOperationException($"Concurrent stock update prevented decrement for product {oi.ProductId}");
                    //    }
                    //}

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
                        // 1) Find coupon by code (case-insensitive)
                        var couponFilter = Builders<Coupon>.Filter.Regex(
                            c => c.Code,
                            new MongoDB.Bson.BsonRegularExpression($"^{Regex.Escape(order.CouponCode.Trim())}$", "i")
                        );

                        var coupon = await _coupons.Find(session, couponFilter).FirstOrDefaultAsync();
                        if (coupon == null || !coupon.IsActive)
                        {
                            throw new InvalidOperationException("Coupon is invalid or not active.");
                        }

                        // 2) Check if user already used this coupon
                        var usageFilter = Builders<CouponUsage>.Filter.And(
                            Builders<CouponUsage>.Filter.Eq(u => u.CouponId, coupon.Id),
                            Builders<CouponUsage>.Filter.Eq(u => u.UserId, userId)
                        );

                        var existingUsage = await _couponUsages.Find(session, usageFilter).FirstOrDefaultAsync();
                        if (existingUsage != null)
                        {
                            throw new InvalidOperationException("Coupon has already been used by this user.");
                        }

                        // 3) Record usage and increment counters (within the same transaction session)
                        var couponUsage = new CouponUsage
                        {
                            CouponId = coupon.Id,
                            UserId = userId,
                            OrderId = order.Id,
                            DiscountAmount = 0m, // frontend handles discount amount; store 0 or leave to later update if you prefer
                            UsedAt = DateTime.UtcNow
                        };
                        await _couponUsages.InsertOneAsync(session, couponUsage);

                        var couponUpdate = Builders<Coupon>.Update
                            .Inc(c => c.UsedCount, 1)
                            .AddToSet(c => c.UsedByUserIds, userId);

                        await _coupons.UpdateOneAsync(session, Builders<Coupon>.Filter.Eq(c => c.Id, coupon.Id), couponUpdate);

                        var orderCouponUpdate = Builders<Order>.Update
                            .Set(o => o.CouponId, coupon.Id)
                            .Set(o => o.CouponAppliedAt, DateTime.UtcNow);

                        await _col.UpdateOneAsync(session, Builders<Order>.Filter.Eq(o => o.Id, order.Id), orderCouponUpdate);

                        order.CouponId = coupon.Id;
                        order.CouponAppliedAt = DateTime.UtcNow;
                    }

                    await session.CommitTransactionAsync();

                    if (string.Equals(order.PaymentMethod, "razorpay", StringComparison.OrdinalIgnoreCase))
                    {
                        bool isInternational = !string.Equals(order.Currency, "INR", StringComparison.OrdinalIgnoreCase);

                        // call helper (returns request/response metadata)
                        var razorResult = await _paymentHelper.CreateRazorpayOrderAsync(order.OrderNumber, order.Total, order.Currency, isInternational);

                        // prepare debug blob to persist in DB
                        var gatewayDebug = new
                        {
                            Request = razorResult.RequestPayload,
                            Response = razorResult.ResponseBody,
                            StatusCode = razorResult.StatusCode,
                            Error = razorResult.ErrorMessage
                        };
                        string gatewayDebugJson = JsonSerializer.Serialize(gatewayDebug);

                        // update order doc: store debug JSON and set razorpay id only if available
                        var payUpdate = Builders<Order>.Update
                            .Set(o => o.PaymentGatewayResponse, gatewayDebugJson)
                            .Set(o => o.PaymentStatus, razorResult.Success ? "Pending" : "Failed");

                        if (razorResult.Success && !string.IsNullOrWhiteSpace(razorResult.RazorpayOrderId))
                        {
                            payUpdate = payUpdate.Set(o => o.RazorpayOrderId, razorResult.RazorpayOrderId);
                            order.RazorpayOrderId = razorResult.RazorpayOrderId;
                            order.PaymentStatus = "Pending";
                        }
                        else
                        {
                            order.PaymentStatus = "Failed";
                        }

                        await _col.UpdateOneAsync(o => o.Id == order.Id, payUpdate);

                        var responseDto = new CreateOrderResponse
                        {
                            Success = razorResult.Success,
                            Id = order.Id?.ToString() ?? string.Empty,
                            OrderId = order.OrderNumber ?? order.Id?.ToString() ?? string.Empty,
                            RazorpayOrderId = razorResult.RazorpayOrderId,
                            Total = order.Total,
                            GiftCardNumber = order.GiftCardNumber,
                        };
                      
                        return responseDto;
                    }

                    // Non-razorpay path: return success wrapper

                    var smsuser = _smsGatewayService.SendOrderConfirmationSmsAsync(order.ShippingAddress.MobileNumner, user.FullName ?? order.ShippingAddress.FullName, order.OrderNumber, order.Total + "");
                    var smsadmin = _smsGatewayService.SendAdminNewOrderSmsAsync(order.ShippingAddress.MobileNumner, user.FullName ?? order.ShippingAddress.FullName, order.OrderNumber, order.Total + "");

                    return new CreateOrderResponse
                    {
                        Success = true,
                        Id = order.Id?.ToString() ?? string.Empty,
                        OrderId = order.OrderNumber ?? order.Id?.ToString() ?? string.Empty,
                        RazorpayOrderId = order.RazorpayOrderId,
                        Total = order.Total,
                        GiftCardNumber = order.GiftCardNumber,
                    };
                }
                catch (MongoWriteException mwx) when (mwx.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    try { await session.AbortTransactionAsync(); } catch { /* ignore if already aborted */ }
                    if (attempt == maxAttempts) throw;
                    continue;
                }
                catch (Exception)
                {
                    try { await session.AbortTransactionAsync(); } catch { /* ignore if already aborted */ }
                    //await TryRollbackInventoryAsync(orderItems);
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



        public async Task<bool> UpdatePaymentStatusAsync(string razorpayOrderId, string razorpayPaymentId, bool isSuccess)
        {
            var filter = Builders<Order>.Filter.Eq(o => o.RazorpayOrderId, razorpayOrderId);
            var update = Builders<Order>.Update
                .Set(o => o.RazorpayPaymentId, razorpayPaymentId)
                .Set(o => o.PaymentStatus, isSuccess ? "Paid" : "Failed")
                .Set(o => o.UpdatedAt, DateTime.UtcNow)
                .Set(o=> o.Status,"Confirmed");

            var result = await _col.UpdateOneAsync(filter, update);

            var order = await _col.Find(o => o.RazorpayOrderId == razorpayOrderId).FirstOrDefaultAsync();
            var user = await _userRepository.GetByIdAsync(order.UserId);


            var smsuser = _smsGatewayService.SendOrderConfirmationSmsAsync(order.ShippingAddress.MobileNumner, user?.FullName ?? order.ShippingAddress.FullName, order.OrderNumber, order.Total + "");
            var smsadmin = _smsGatewayService.SendAdminNewOrderSmsAsync(order.ShippingAddress.MobileNumner, user?.FullName ?? order.ShippingAddress.FullName,  order.OrderNumber,order.Total + "");

            return result.ModifiedCount > 0;
        }

        public async Task<bool> updateOrderForReturn(string orderNumber, List<OrderItem> returnItems)
        {
            if (string.IsNullOrWhiteSpace(orderNumber)) throw new ArgumentException(nameof(orderNumber));
            if (returnItems == null || returnItems.Count == 0) return false;

            // find order
            var filter = Builders<Order>.Filter.Eq(o => o.OrderNumber, orderNumber);
            var order = await _col.Find(filter).FirstOrDefaultAsync();
            if (order == null) return false;

            // Build a lookup for fast matching by ProductObjectId
            var returnLookup = returnItems
                .Where(r => r?.ProductObjectId != null)
                .ToDictionary(r => r.ProductObjectId, r => r);

            var changed = false;

            foreach (var item in order.Items)
            {
                if (item?.ProductObjectId == null) continue;

                if (returnLookup.TryGetValue(item.ProductObjectId, out var retItem))
                {
                    // mark returned and copy reason
                    item.IsReturned = true;
                    item.ReasonForReturn = retItem.ReasonForReturn;
                    changed = true;
                }
            }

            if (!changed) return false; // nothing to update

            // Persist the modified order back to MongoDB
            var replaceResult = await _col.ReplaceOneAsync(filter, order);

            return replaceResult.IsAcknowledged && replaceResult.ModifiedCount > 0;
        }

        public async Task<bool> UpdateDeliveryDetailsAsync(
            string referenceId,
            DeliveryDetails request)
        {
            var delivery = new DeliveryDetails
            {
                Awb = request.Awb, // only if present
                Courier = "DELHIVERY",
                IsCod = request.IsCod,
                CodAmount = request.IsCod ? request.CodAmount : 0,
                IsInternational = request.IsInternational,
                Status = "READY_TO_SHIP",
                Type =  request.Type,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var update = Builders<Order>.Update
                .Set(x => x.deliveryDetails, delivery)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _col.UpdateOneAsync(
                x => x.Id == referenceId,
                update);

            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateDeliveryStatusAsync(
    string orderId,
    string status)
        {
            var update = Builders<Order>.Update
                .Set(x => x.deliveryDetails.Status, status)
                .Set(x => x.deliveryDetails.UpdatedAt, DateTime.UtcNow);

            var result = await _col.UpdateOneAsync(
                x => x.Id == orderId,
                update);

            return result.MatchedCount > 0;
        }

        public async Task<bool> UpdateDeliveryStatusAsync(UpdateOrderStatusAdmin request)
        {
            var updates = new List<UpdateDefinition<Order>>();

            // deliveryDetails updates (only if orderStatus present)
            if (!string.IsNullOrWhiteSpace(request.orderStatus))
            {
                updates.Add(Builders<Order>.Update
                    .Set(x => x.deliveryDetails.Status, request.orderStatus)
                    .Set(x => x.deliveryDetails.UpdatedAt, DateTime.UtcNow));
            }

            if (!string.IsNullOrWhiteSpace(request.orderStatus))
            {
                updates.Add(Builders<Order>.Update
                    .Set(x => x.Status, request.orderStatus)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
            }

            // payment status update (only if paymentStatus present)
            if (!string.IsNullOrWhiteSpace(request.paymentStatus))
            {
                updates.Add(Builders<Order>.Update
                    .Set(x => x.PaymentStatus, request.paymentStatus)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow));
            }

            // nothing to update
            if (!updates.Any())
                return false;

            var update = Builders<Order>.Update.Combine(updates);

            var result = await _col.UpdateOneAsync(
                x => x.Id == request.id,
                update
            );

            if (request.orderStatus == "Cancelled")
            {
                var order = await _col.Find(o => o.Id == request.id).FirstOrDefaultAsync();
                var user = await _userRepository.GetByIdAsync(order.UserId);
                var smsuser = _smsGatewayService.SendOrderCancellationSmsAsync(order.ShippingAddress.MobileNumner, user?.FullName??order.ShippingAddress.FullName, order.OrderNumber);

            }

            return result.MatchedCount > 0;
        }


        public async Task<Order?> GetByAwbAsync(string awb)
        {
            if (string.IsNullOrWhiteSpace(awb))
                return null;

            return await _col.Find(x =>
                x.deliveryDetails != null &&
                x.deliveryDetails.Awb == awb
            ).FirstOrDefaultAsync();
        }

    }
}
