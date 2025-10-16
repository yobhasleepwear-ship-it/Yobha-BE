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
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";
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
        [HttpPost]
        [Authorize]
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Create([FromBody] CreateOrderRequest? request)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";

            // fetch cart
            var cartItems = await _cartRepo.GetForUserAsync(userId);
            if (cartItems == null || !cartItems.Any())
                return BadRequest(ApiResponse<object>.Fail("Cart is empty", null, HttpStatusCode.BadRequest));

            // Build order items and subtotal
            var orderItems = new List<OrderItem>();
            decimal subTotal = 0m;
            const string currency = "INR";

            foreach (var cart in cartItems)
            {
                if (cart == null) continue;

                var product = await _productRepo.GetByIdAsync(cart.ProductObjectId);
                if (product == null)
                    return BadRequest(ApiResponse<object>.Fail($"Product not found: {cart.ProductId}", null, HttpStatusCode.BadRequest));

                decimal unitPrice = product.Price > 0 ? product.Price : cart.Price;
                if (unitPrice <= 0)
                    return BadRequest(ApiResponse<object>.Fail($"Invalid price for product {cart.ProductId}", null, HttpStatusCode.BadRequest));

                if (product.Stock > 0 && product.Stock < cart.Quantity)
                    return BadRequest(ApiResponse<object>.Fail($"Insufficient stock for product {cart.ProductId}", null, HttpStatusCode.BadRequest));

                decimal lineTotal = Decimal.Round(unitPrice * cart.Quantity, 2, MidpointRounding.AwayFromZero);

                var oi = new OrderItem
                {
                    ProductId = cart.ProductId,
                    ProductObjectId = cart.ProductObjectId,
                    ProductName = cart.ProductName,
                    VariantSku = cart.VariantSku,
                    VariantId = cart.Snapshot?.VariantId,
                    Quantity = cart.Quantity,
                    UnitPrice = Decimal.Round(unitPrice, 2),
                    LineTotal = lineTotal,
                    CompareAtPrice = cart.Snapshot?.CompareAtPrice,
                    Currency = currency,
                    ThumbnailUrl = cart.Snapshot?.ThumbnailUrl,
                    Slug = cart.Snapshot?.Slug
                };

                orderItems.Add(oi);
                subTotal += lineTotal;
            }

            if (!orderItems.Any())
                return BadRequest(ApiResponse<object>.Fail("No valid items in cart to create order", null, HttpStatusCode.BadRequest));

            // shipping & tax placeholders
            decimal shipping = 0m;
            decimal tax = 0m;

            // Coupon validation (validate-only)
            decimal couponDiscount = 0m;
            string? appliedCouponId = null;
            string? appliedCouponCode = null;
            CouponValidationResult? couponPreview = null;
            if (!string.IsNullOrWhiteSpace(request?.CouponCode))
            {
                couponPreview = await _couponService.ValidateOnlyAsync(request.CouponCode!.Trim(), userId, subTotal);
                if (!couponPreview.IsValid)
                    return BadRequest(ApiResponse<object>.Fail(couponPreview.ErrorMessage, null, HttpStatusCode.BadRequest));

                couponDiscount = Decimal.Round(couponPreview.DiscountAmount, 2, MidpointRounding.AwayFromZero);
                appliedCouponId = couponPreview.Coupon?.Id;
                appliedCouponCode = couponPreview.Coupon?.Code;
            }

            // Loyalty discount (client signals intent by passing a non-null LoyaltyDiscountAmount).
            // We do NOT convert points here; we will remove all user's points later and apply the requested amount.
            decimal loyaltyDiscountAmount = request?.LoyaltyDiscountAmount ?? 0m;
            decimal pointsToDeduct = 0;

            // Compute totals (coupon + loyalty)
            decimal discount = couponDiscount + loyaltyDiscountAmount;
            var grandTotal = Decimal.Round(subTotal + shipping + tax - discount, 2, MidpointRounding.AwayFromZero);
            if (grandTotal < 0) grandTotal = 0m;

            // Build order object (coupon + loyalty metadata)
            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                SubTotal = Decimal.Round(subTotal, 2),
                Shipping = shipping,
                Tax = tax,
                Discount = discount,
                Total = grandTotal,
                Currency = currency,
                Status = "Pending",
                PaymentMethod = string.IsNullOrWhiteSpace(request?.PaymentMethod) ? "COD" : request.PaymentMethod,
                PaymentStatus = "Pending",
                CouponCode = appliedCouponCode,
                CouponId = appliedCouponId,
                CouponAppliedAt = appliedCouponId != null ? DateTime.UtcNow : null,
                CouponUsageRecorded = false,
                LoyaltyDiscountAmount = 0m,   // set after successful loyalty removal
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null,
                ShippingAddress = request?.ShippingAddress
            };

            try
            {
                // Save initial order
                var created = await _orderRepo.CreateAsync(order);

                // Reserve / decrement stock
                var decremented = new List<(string productObjectId, int qty)>();
                try
                {
                    foreach (var item in created.Items)
                    {
                        var ok = await _productRepo.DecrementStockAsync(item.ProductObjectId, item.Quantity);
                        if (!ok) throw new InvalidOperationException($"Failed to reserve stock for product {item.ProductId}");
                        decremented.Add((item.ProductObjectId, item.Quantity));
                    }
                }
                catch (Exception decEx)
                {
                    // rollback previous decrements
                    foreach (var d in decremented)
                    {
                        try { await _productRepo.IncrementStockAsync(d.productObjectId, d.qty); } catch { }
                    }

                    // delete created order
                    try { await _orderRepo.DeleteAsync(created.Id); } catch (Exception delEx) { _logger.LogError(delEx, "Failed to delete order after stock reservation failure"); }

                    _logger.LogWarning(decEx, "Stock reservation failed for order {OrderId}", created.Id);
                    return BadRequest(ApiResponse<object>.Fail("Failed to reserve stock for one or more items. Please try again.", null, HttpStatusCode.BadRequest));
                }

                // Now: attempt to claim coupon (if any)
                Coupon? claimedCoupon = null;
                if (!string.IsNullOrWhiteSpace(appliedCouponCode))
                {
                    var claim = await _couponService.TryClaimAndRecordAsync(appliedCouponCode!, userId, created.Id, couponDiscount);
                    if (!claim.Success)
                    {
                        // Undo stock and order
                        foreach (var d in decremented) { try { await _productRepo.IncrementStockAsync(d.productObjectId, d.qty); } catch { } }
                        try { await _orderRepo.DeleteAsync(created.Id); } catch { }
                        return BadRequest(ApiResponse<object>.Fail(claim.Error ?? "Failed to claim coupon", null, HttpStatusCode.BadRequest));
                    }
                    claimedCoupon = claim.Coupon;
                    // mark order as coupon usage recorded
                    created.CouponUsageRecorded = true;
                    await _orderRepo.UpdateAsync(created.Id, created);
                }

                // Now: remove ALL user's loyalty points if client signalled by passing LoyaltyDiscountAmount
                if (request?.LoyaltyDiscountAmount != null && request.LoyaltyDiscountAmount > 0m)
                {
                    // Fetch current user points
                    var user = await _userRepo.GetByIdAsync(userId);
                    if (user == null)
                    {
                        // compensation: undo coupon claim (if any), rollback stock, delete order
                        if (claimedCoupon != null && !string.IsNullOrWhiteSpace(claimedCoupon.Id))
                        {
                            try { await _couponRepo.UndoClaimAsync(claimedCoupon.Id!, userId); } catch { }
                        }
                        foreach (var d in decremented) { try { await _productRepo.IncrementStockAsync(d.productObjectId, d.qty); } catch { } }
                        try { await _orderRepo.DeleteAsync(created.Id); } catch { }
                        return BadRequest(ApiResponse<object>.Fail("User not found", null, HttpStatusCode.BadRequest));
                    }

                    // Remove all available points (pointsToDeduct = current points)
                    pointsToDeduct = user.LoyaltyPoints??0m;

                    if (pointsToDeduct > 0)
                    {
                        var deducted = await _userRepo.TryDeductLoyaltyPointsAsync(userId, pointsToDeduct);
                        if (!deducted)
                        {
                            // compensation: undo coupon claim (if any), rollback stock, delete order
                            if (claimedCoupon != null && !string.IsNullOrWhiteSpace(claimedCoupon.Id))
                            {
                                try { await _couponRepo.UndoClaimAsync(claimedCoupon.Id!, userId); } catch { }
                            }

                            foreach (var d in decremented) { try { await _productRepo.IncrementStockAsync(d.productObjectId, d.qty); } catch { } }
                            try { await _orderRepo.DeleteAsync(created.Id); } catch { }

                            return BadRequest(ApiResponse<object>.Fail("Failed to remove loyalty points (concurrent change).", null, HttpStatusCode.BadRequest));
                        }
                    }

                    // Apply the exact amount passed by client (no conversion)
                    created.LoyaltyDiscountAmount = Decimal.Round(request.LoyaltyDiscountAmount.Value, 2, MidpointRounding.AwayFromZero);

                    // Update order's discount/total (add loyalty discount to existing discount)
                    created.Discount = Decimal.Round(created.Discount + (created.LoyaltyDiscountAmount??0m), 2, MidpointRounding.AwayFromZero);
                    var subtotalWithCharges = created.SubTotal + created.Shipping + created.Tax;
                    created.Total = Decimal.Round(Math.Max(subtotalWithCharges - created.Discount, 0m), 2, MidpointRounding.AwayFromZero);

                    // Persist the order update
                    await _orderRepo.UpdateAsync(created.Id, created);
                }

                // clear the cart (only after stock reserved and discounts applied)
                await _cartRepo.ClearAsync(userId);

                // handle COD / Razorpay
                if (order.PaymentMethod?.ToLowerInvariant() == "cod")
                {
                    var resp = ApiResponse<Order>.Created(created, "Order created; pay on delivery (COD).");
                    return CreatedAtAction(nameof(Get), new { id = created.Id }, resp);
                }

                if (order.PaymentMethod?.ToLowerInvariant() == "razorpay")
                {
                    var rpOrderResp = await _razorpayService.CreateOrderAsync(created.Total, created.Currency ?? "INR", created.Id);
                    created.RazorpayOrderId = rpOrderResp.id;
                    await _orderRepo.UpdateAsync(created.Id, created);

                    var clientPayload = new
                    {
                        Order = created,
                        Razorpay = new
                        {
                            orderId = rpOrderResp.id,
                            amount = rpOrderResp.amount,
                            currency = rpOrderResp.currency,
                            key = _razorpayService.KeyId
                        }
                    };

                    var resp = ApiResponse<object>.Created(clientPayload, "Order created. Use razorpay order to open checkout.");
                    return CreatedAtAction(nameof(Get), new { id = created.Id }, resp);
                }

                var createdResp = ApiResponse<object>.Created(created, "Order created successfully");
                return CreatedAtAction(nameof(Get), new { id = created.Id }, createdResp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create order for user {UserId}", userId);
                return StatusCode((int)HttpStatusCode.InternalServerError,
                    ApiResponse<object>.Fail("Failed to create order. Please try again.", null, HttpStatusCode.InternalServerError));
            }
        }

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
    }
}
