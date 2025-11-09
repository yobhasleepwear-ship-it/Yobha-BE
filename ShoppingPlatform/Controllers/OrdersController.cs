using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShoppingPlatform.Dto;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services; // for IRazorpayService and ICouponService
using ShoppingPlatform.Helpers;
using System.Security.Claims;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderRepository _orderRepo;
        private readonly ICartRepository _cartRepo;
        private readonly IProductRepository _productRepo;
        private readonly ICouponService _couponService;
        private readonly IRazorpayService _razorpayService;
        private readonly ILogger<OrdersController> _logger;
        private readonly UserRepository _userRepo;
        private readonly ICouponRepository _couponRepo;

        public OrdersController(
            IOrderRepository orderRepo,
            ICartRepository cartRepo,
            IProductRepository productRepo,
            ICouponService couponService,
            IRazorpayService razorpayService,
            ILogger<OrdersController> logger,
            UserRepository userRepo,
            ICouponRepository couponRepo)
        {
            _orderRepo = orderRepo;
            _cartRepo = cartRepo;
            _productRepo = productRepo;
            _couponService = couponService;
            _razorpayService = razorpayService;
            _logger = logger;
            _userRepo = userRepo;
            _couponRepo = couponRepo;
        }

        // -------------------------------------------
        // GET: api/orders
        // -------------------------------------------
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<ApiResponse<IEnumerable<Order>>>> GetForUser()
        {
            var userId = User.GetUserIdOrAnonymous();
            var list = await _orderRepo.GetForUserAsync(userId);

            var response = ApiResponse<IEnumerable<Order>>.Ok(list, "Orders fetched successfully");
            return Ok(response);
        }

        // -------------------------------------------
        // GET: api/orders/{id}
        // -------------------------------------------
        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<Order>>> Get(string id)
        {
            var order = await _orderRepo.GetByIdAsync(id);
            if (order == null)
            {
                var notFoundResponse = ApiResponse<Order>.Fail("Order not found", null, HttpStatusCode.NotFound);
                return NotFound(notFoundResponse);
            }

            var response = ApiResponse<Order>.Ok(order, "Order fetched successfully");
            return Ok(response);
        }

        // -------------------------------------------
        // POST: api/orders
        // Automatically builds the order from the user’s cart.
        // -------------------------------------------
        //[HttpPost]
        //[Authorize]
        //public async Task<ActionResult<ApiResponse<object>>> Create([FromBody] CreateOrderRequest? request)
        //{
        //    var userId = User.GetUserIdOrAnonymous();

        //    // 1️⃣ Get user's cart
        //    var cartItems = await _cartRepo.GetForUserAsync(userId);
        //    if (cartItems == null || !cartItems.Any())
        //        return BadRequest(ApiResponse<object>.Fail("Cart is empty", null, HttpStatusCode.BadRequest));

        //    var orderItems = new List<OrderItem>();
        //    decimal subTotal = 0m;
        //    const string defaultCurrency = "INR";

        //    // 2️⃣ Build order items + subtotal
        //    foreach (var cart in cartItems)
        //    {
        //        var product = await _productRepo.GetByIdAsync(cart.ProductObjectId);
        //        if (product == null)
        //            return BadRequest(ApiResponse<object>.Fail($"Product not found: {cart.ProductId}", null, HttpStatusCode.BadRequest));

        //        // Find matching PriceList entry (size + currency)
        //        var priceEntry = product.PriceList?.FirstOrDefault(p =>
        //            p.Size.Equals(cart.VariantSku, StringComparison.OrdinalIgnoreCase) &&
        //            p.Currency.Equals(cart.Currency ?? defaultCurrency, StringComparison.OrdinalIgnoreCase));

        //        if (priceEntry == null)
        //            return BadRequest(ApiResponse<object>.Fail($"Size '{cart.VariantSku}' not found for {cart.ProductName}.", null, HttpStatusCode.BadRequest));

        //        if (priceEntry.Quantity < cart.Quantity)
        //            return BadRequest(ApiResponse<object>.Fail($"Insufficient stock for {cart.ProductName} ({cart.VariantSku})", null, HttpStatusCode.BadRequest));

        //        // Determine base price
        //        var unitPrice = priceEntry.PriceAmount;

        //        // Determine shipping cost using CountryPrices (if applicable)
        //        decimal shippingPrice = 0m;
        //        if (product.CountryPrices?.Any() == true)
        //        {
        //            var matchedCountry = product.CountryPrices.FirstOrDefault(cp =>
        //                cp.Currency.Equals(cart.Currency ?? defaultCurrency, StringComparison.OrdinalIgnoreCase));

        //            if (matchedCountry != null)
        //                shippingPrice = matchedCountry.PriceAmount;
        //        }

        //        // Calculate totals
        //        var lineTotal = Math.Round(unitPrice * cart.Quantity, 2);

        //        orderItems.Add(new OrderItem
        //        {
        //            ProductId = cart.ProductId,
        //            ProductObjectId = cart.ProductObjectId,
        //            ProductName = cart.ProductName,
        //            Quantity = cart.Quantity,
        //            UnitPrice = unitPrice,
        //            LineTotal = lineTotal,
        //            Currency = cart.Currency ?? defaultCurrency,
        //            ThumbnailUrl = cart.Snapshot?.ThumbnailUrl,
        //            Slug = cart.Snapshot?.Slug
        //        });

        //        subTotal += lineTotal;
        //    }

        //    // 3️⃣ Shipping & Tax
        //    // Aggregate shipping cost (sum of individual shipping if any)
        //    decimal shipping = orderItems.Sum(i => i.Currency == "INR" ? 0 : 0); // default zero, can customize later

        //    // If using CountryPrices for shipping:
        //    if (cartItems.Any())
        //    {
        //        var shippingByCurrency = cartItems.Select(c =>
        //        {
        //            var product = _productRepo.GetByIdAsync(c.ProductObjectId).Result;
        //            var matchedCountry = product?.CountryPrices?.FirstOrDefault(cp =>
        //                cp.Currency.Equals(c.Currency ?? defaultCurrency, StringComparison.OrdinalIgnoreCase));
        //            return matchedCountry?.PriceAmount ?? 0m;
        //        }).ToList();

        //        shipping = shippingByCurrency.Sum();
        //    }

        //    decimal tax = 0m;

        //    // 4️⃣ Coupon validation
        //    decimal couponDiscount = 0m;
        //    string? appliedCouponId = null;
        //    string? appliedCouponCode = null;
        //    CouponValidationResult? couponPreview = null;

        //    if (!string.IsNullOrWhiteSpace(request?.CouponCode))
        //    {
        //        couponPreview = await _couponService.ValidateOnlyAsync(request.CouponCode.Trim(), userId, subTotal);
        //        if (!couponPreview.IsValid)
        //            return BadRequest(ApiResponse<object>.Fail(couponPreview.ErrorMessage, null, HttpStatusCode.BadRequest));

        //        couponDiscount = Math.Round(couponPreview.DiscountAmount, 2);
        //        appliedCouponId = couponPreview.Coupon?.Id;
        //        appliedCouponCode = couponPreview.Coupon?.Code;
        //    }

        //    // 5️⃣ Loyalty discount
        //    decimal loyaltyDiscountAmount = request?.LoyaltyDiscountAmount ?? 0m;

        //    // 6️⃣ Compute totals
        //    decimal discount = couponDiscount + loyaltyDiscountAmount;
        //    var grandTotal = Math.Round(subTotal + shipping + tax - discount, 2, MidpointRounding.AwayFromZero);
        //    if (grandTotal < 0) grandTotal = 0m;

        //    // 7️⃣ Build order
        //    var order = new Order
        //    {
        //        UserId = userId,
        //        Items = orderItems,
        //        SubTotal = subTotal,
        //        Shipping = shipping,
        //        Tax = tax,
        //        Discount = discount,
        //        Total = grandTotal,
        //        Currency = defaultCurrency,
        //        Status = "Pending",
        //        PaymentMethod = request?.PaymentMethod ?? "COD",
        //        PaymentStatus = "Pending",
        //        CouponCode = appliedCouponCode,
        //        CouponId = appliedCouponId,
        //        CouponAppliedAt = appliedCouponId != null ? DateTime.UtcNow : null,
        //        CouponUsageRecorded = false,
        //        LoyaltyDiscountAmount = loyaltyDiscountAmount,
        //        CreatedAt = DateTime.UtcNow,
        //        ShippingAddress = request?.ShippingAddress
        //    };

        //    try
        //    {
        //        // 8️⃣ Save initial order
        //        var created = await _orderRepo.CreateAsync(order);

        //        // 9️⃣ Decrement stock
        //        var decremented = new List<(string productObjectId, string size, int qty, string currency)>();
        //        try
        //        {
        //            foreach (var item in created.Items)
        //            {
        //                var ok = await _productRepo.DecrementStockAsync(item.ProductObjectId, item.VariantSku, item.Currency, item.Quantity);
        //                if (!ok)
        //                    throw new InvalidOperationException($"Failed to reserve stock for {item.ProductName} ({item.VariantSku})");

        //                decremented.Add((item.ProductObjectId, item.VariantSku, item.Quantity, item.Currency));
        //            }
        //        }
        //        catch (Exception stockEx)
        //        {
        //            foreach (var d in decremented)
        //                try { await _productRepo.IncrementStockAsync(d.productObjectId, d.size, d.currency, d.qty); } catch { }

        //            await _orderRepo.DeleteAsync(created.Id);
        //            _logger.LogWarning(stockEx, "Stock reservation failed for order {OrderId}", created.Id);
        //            return BadRequest(ApiResponse<object>.Fail("Failed to reserve stock.", null, HttpStatusCode.BadRequest));
        //        }

        //        // 🔟 Coupon + Loyalty logic (same as before)
        //        if (!string.IsNullOrWhiteSpace(appliedCouponCode))
        //        {
        //            var claim = await _couponService.TryClaimAndRecordAsync(appliedCouponCode!, userId, created.Id, couponDiscount);
        //            if (!claim.Success)
        //            {
        //                foreach (var d in decremented)
        //                    try { await _productRepo.IncrementStockAsync(d.productObjectId, d.size, d.currency, d.qty); } catch { }

        //                await _orderRepo.DeleteAsync(created.Id);
        //                return BadRequest(ApiResponse<object>.Fail(claim.Error ?? "Failed to claim coupon", null, HttpStatusCode.BadRequest));
        //            }

        //            created.CouponUsageRecorded = true;
        //            await _orderRepo.UpdateAsync(created.Id, created);
        //        }

        //        // Loyalty points deduction (unchanged)
        //        if (loyaltyDiscountAmount > 0)
        //        {
        //            var user = await _userRepo.GetByIdAsync(userId);
        //            if (user?.LoyaltyPoints > 0)
        //                await _userRepo.TryDeductLoyaltyPointsAsync(userId, user.LoyaltyPoints.Value);
        //        }

        //        // Clear user cart
        //        await _cartRepo.ClearAsync(userId);

        //        // Payment handling
        //        if (created.PaymentMethod.Equals("cod", StringComparison.OrdinalIgnoreCase))
        //        {
        //            return CreatedAtAction(nameof(Get), new { id = created.Id },
        //                ApiResponse<Order>.Created(created, "Order created; pay on delivery."));
        //        }

        //        if (created.PaymentMethod.Equals("razorpay", StringComparison.OrdinalIgnoreCase))
        //        {
        //            var rpOrder = await _razorpayService.CreateOrderAsync(created.Total, created.Currency, created.Id);
        //            created.RazorpayOrderId = rpOrder.id;
        //            await _orderRepo.UpdateAsync(created.Id, created);

        //            var response = new
        //            {
        //                Order = created,
        //                Razorpay = new
        //                {
        //                    orderId = rpOrder.id,
        //                    amount = rpOrder.amount,
        //                    currency = rpOrder.currency,
        //                    key = _razorpayService.KeyId
        //                }
        //            };

        //            return CreatedAtAction(nameof(Get), new { id = created.Id },
        //                ApiResponse<object>.Created(response, "Order created, proceed with Razorpay checkout."));
        //        }

        //        return CreatedAtAction(nameof(Get), new { id = created.Id },
        //            ApiResponse<Order>.Created(created, "Order created successfully"));
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Order creation failed for user {UserId}", userId);
        //        return StatusCode(500, ApiResponse<object>.Fail("Order creation failed.", null, HttpStatusCode.InternalServerError));
        //    }
        //}

        // -------------------------------------------
        // PATCH: api/orders/{id}/status
        // Admin only
        // -------------------------------------------
        [HttpPatch("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<OrderStatusResponse>>> UpdateStatus(
            string id,
            [FromBody] UpdateOrderStatusRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<OrderStatusResponse>.Fail("Invalid request", null, HttpStatusCode.BadRequest));

            // update status in repo
            var updated = await _orderRepo.UpdateStatusAsync(id, request.Status);
            if (!updated)
                return NotFound(ApiResponse<OrderStatusResponse>.Fail("Order not found", null, HttpStatusCode.NotFound));

            // optional: fetch order to return updated info or build response
            var order = await _orderRepo.GetByIdAsync(id);
            var responsePayload = new OrderStatusResponse
            {
                OrderId = id,
                Status = request.Status,
                UpdatedAt = order?.UpdatedAt ?? DateTime.UtcNow,
                UpdatedBy = User?.FindFirst("sub")?.Value,
                Note = request.Note
            };

            // optionally trigger notifications if request.NotifyCustomer == true

            return Ok(ApiResponse<OrderStatusResponse>.Ok(responsePayload, "Status updated successfully"));
        }

        [HttpGet("GetAllOrdersAdmin")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllOrdersAdmin(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sort = "createdAt_desc",
            [FromQuery] string? Id = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            CancellationToken ct = default)
        {
            var filter = new OrderFilter
            {
                Id = Id,
                From = from,
                To = to
            };

            var pagedResult = await _orderRepo.GetOrdersAdminAsync(page, pageSize, sort, filter, ct);

            var pagination = new PaginationResponse
            {
                PageNumber = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalRecords = (int)pagedResult.TotalCount,
                TotalPages = pagedResult.TotalPages
            };

            var response = new ApiResponse<List<Order>>
            {
                Success = true,
                Status = HttpStatusCode.OK,
                Message = "Orders fetched successfully",
                Data = pagedResult.Items.ToList(),
                Pagination = pagination
            };

            return Ok(response);
        }


        [HttpPost]
        [Authorize]

        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequestV2 req)
        {
            if (req == null) return BadRequest("Request body required");

            // get user id from claims (adjust to your auth)
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

            _logger.LogInformation("CreateOrder called by user {UserId} paymentMethod={PaymentMethod}", userId, req.PaymentMethod);

            CreateOrderResponse result;
            try
            {
                result = await _orderRepo.CreateOrderAsync(req, userId);
            }
            catch (ArgumentException aex)
            {
                _logger.LogWarning(aex, "Bad request in CreateOrder");
                return BadRequest(aex.Message);
            }
            catch (InvalidOperationException ioex)
            {
                _logger.LogError(ioex, "Order creation failed");
                return BadRequest(ioex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error creating order");
                return StatusCode(500, "Internal Server Error");
            }

            // If you want Created and a location header (adjust GetOrder route to exist)
            if (result.Success)
            {
                var idForLocation = string.IsNullOrWhiteSpace(result.Id) ? result.OrderId : result.Id;
                return CreatedAtAction(nameof(Get), new { id = idForLocation }, result);
            }

            // Return the result anyway (will include RazorpayDebug)
            return BadRequest(result);
        }
    }
}
