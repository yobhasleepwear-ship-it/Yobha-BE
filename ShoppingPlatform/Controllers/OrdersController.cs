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

        public OrdersController(
            IOrderRepository orderRepo,
            ICartRepository cartRepo,
            IProductRepository productRepo,
            ICouponService couponService,
            IRazorpayService razorpayService,
            ILogger<OrdersController> logger)
        {
            _orderRepo = orderRepo;
            _cartRepo = cartRepo;
            _productRepo = productRepo;
            _couponService = couponService;
            _razorpayService = razorpayService;
            _logger = logger;
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

                // NOTE: product.Stock can be null/0. We treat null as unlimited if that's your convention.
                if (product.Stock>0 && product.Stock < cart.Quantity)
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
            if (request?.ShippingAddress != null)
            {
                // shipping = await _shippingService.GetRateAsync(request.ShippingAddress, orderItems);
                // tax = await _taxService.CalculateTaxAsync(request.ShippingAddress, orderItems, subTotal);
            }

            // coupon validation (validate-only)
            decimal discount = 0m;
            string? appliedCouponId = null;
            string? appliedCouponCode = null;
            if (!string.IsNullOrWhiteSpace(request?.CouponCode))
            {
                var couponPreview = await _couponService.ValidateOnlyAsync(request.CouponCode!.Trim(), userId, subTotal);
                if (!couponPreview.IsValid)
                    return BadRequest(ApiResponse<object>.Fail(couponPreview.ErrorMessage, null, HttpStatusCode.BadRequest));

                discount = Decimal.Round(couponPreview.DiscountAmount, 2, MidpointRounding.AwayFromZero);
                appliedCouponId = couponPreview.Coupon?.Id;
                appliedCouponCode = couponPreview.Coupon?.Code;
            }

            var grandTotal = Decimal.Round(subTotal + shipping + tax - discount, 2, MidpointRounding.AwayFromZero);
            if (grandTotal < 0) grandTotal = 0m;

            // Build order object - PaymentMethod may be "COD" or "razorpay"
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null,
                ShippingAddress = request?.ShippingAddress
            };

            try
            {
                // Save initial order
                var created = await _orderRepo.CreateAsync(order);

                // ---------------------------
                // IMPORTANT: decrement stock
                // ---------------------------
                // We'll try to atomically decrement each product's stock using IProductRepository.DecrementStockAsync.
                // If any decrement fails, rollback previous decrements, delete the created order, and return error.

                var decremented = new List<(string productObjectId, int qty)>();
                try
                {
                    foreach (var item in created.Items)
                    {
                        // Attempt an atomic decrement. Repository method should check stock >= qty and update atomically.
                        // Expected signature: Task<bool> DecrementStockAsync(string productObjectId, int quantity)
                        var ok = await _productRepo.DecrementStockAsync(item.ProductObjectId, item.Quantity);
                        if (!ok)
                        {
                            // insufficient stock or concurrent update prevented decrement
                            throw new InvalidOperationException($"Failed to reserve stock for product {item.ProductId}");
                        }

                        // track successful decrement for potential rollback
                        decremented.Add((item.ProductObjectId, item.Quantity));
                    }
                }
                catch (Exception decEx)
                {
                    // rollback previous decrements
                    foreach (var d in decremented)
                    {
                        try
                        {
                            // Best-effort: increment back the stock. Expected signature: Task IncrementStockAsync(...)
                            await _productRepo.IncrementStockAsync(d.productObjectId, d.qty);
                        }
                        catch (Exception rollEx)
                        {
                            _logger.LogError(rollEx, "Failed to rollback stock for product {ProductObjectId}", d.productObjectId);
                            // don't throw here - we continue trying to rollback others
                        }
                    }

                    // delete the created order to avoid dangling order without stock reservation
                    try
                    {
                        await _orderRepo.DeleteAsync(created.Id);
                    }
                    catch (Exception delEx)
                    {
                        _logger.LogError(delEx, "Failed to delete order {OrderId} after stock reservation failure", created.Id);
                        // We will still return error to client — but admin may need to clean this up manually
                    }

                    _logger.LogWarning(decEx, "Stock reservation failed for order {OrderId}", created.Id);
                    return BadRequest(ApiResponse<object>.Fail("Failed to reserve stock for one or more items. Please try again.", null, HttpStatusCode.BadRequest));
                }

                // ---------------------------
                // All stock decremented successfully
                // ---------------------------

                // OPTIONAL: If you prefer to reserve stock for Razorpay (and only decrement on payment success),
                // replace the DecrementStockAsync call above with a ReserveStockAsync that uses TTL and then ConfirmStockAsync on webhook.
                // Current implementation decrements immediately for both COD and razorpay flows; ensure you restore if payment fails.

                // clear the cart (only after stock reserved)
                await _cartRepo.ClearAsync(userId);

                // handle payment flows (COD / razorpay)
                if (order.PaymentMethod?.ToLowerInvariant() == "cod")
                {
                    // For COD we already decremented stock and created the order
                    var resp = ApiResponse<Order>.Created(created, "Order created; pay on delivery (COD).");
                    return CreatedAtAction(nameof(Get), new { id = created.Id }, resp);
                }

                if (order.PaymentMethod?.ToLowerInvariant() == "razorpay")
                {
                    var rpOrderResp = await _razorpayService.CreateOrderAsync(created.Total, created.Currency ?? "INR", created.Id);

                    // Save razorpay order id reference on the order model and persist
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
