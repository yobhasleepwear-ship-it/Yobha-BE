using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderRepository _orderRepo;
        private readonly ICartRepository _cartRepo;
        private readonly IProductRepository _productRepo;
        private readonly Services.ICouponService _couponService;

        public OrdersController(IOrderRepository orderRepo, ICartRepository cartRepo, IProductRepository productRepo, Services.ICouponService couponService)
        {
            _orderRepo = orderRepo;
            _cartRepo = cartRepo;
            _productRepo = productRepo;
            _couponService = couponService;
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
        public async Task<ActionResult<ApiResponse<Order>>> Create([FromBody] ShoppingPlatform.DTOs.CreateOrderRequest? request)
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";

            // fetch cart
            var cartItems = await _cartRepo.GetForUserAsync(userId);
            if (cartItems == null || !cartItems.Any())
            {
                var badResponse = ApiResponse<Order>.Fail("Cart is empty", null, HttpStatusCode.BadRequest);
                return BadRequest(badResponse);
            }

            // build order items & subtotal (existing logic)
            var orderItems = new List<OrderItem>();
            decimal subTotal = 0m;
            string currency = "INR";

            foreach (var cartItem in cartItems)
            {
                var snap = cartItem.Snapshot;
                // ... same hydration logic as before (reuse your code)
                // For brevity, assume you copy the existing item-building code here exactly as before
                // and add to orderItems and subTotal.
            }

            if (!orderItems.Any())
            {
                var badResponse = ApiResponse<Order>.Fail("No valid items in cart to create order", null, HttpStatusCode.BadRequest);
                return BadRequest(badResponse);
            }

            // compute shipping/tax as before
            decimal shipping = 0m;
            decimal tax = 0m;

            decimal discount = 0m;
            string? appliedCouponId = null;
            string? appliedCouponCode = null;

            // Validate coupon if provided (validate-only, do NOT record usage here)
            if (!string.IsNullOrWhiteSpace(request?.CouponCode))
            {
                var couponPreview = await _couponService.ValidateOnlyAsync(request.CouponCode!.Trim(), userId, subTotal);
                if (!couponPreview.IsValid)
                {
                    return BadRequest(ApiResponse<Order>.Fail(couponPreview.ErrorMessage, null, HttpStatusCode.BadRequest));
                }

                discount = couponPreview.DiscountAmount;
                appliedCouponId = couponPreview.Coupon?.Id;
                appliedCouponCode = couponPreview.Coupon?.Code;
            }

            var grandTotal = Decimal.Round(subTotal + shipping + tax - discount, 2);

            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                SubTotal = Decimal.Round(subTotal, 2),
                Shipping = shipping,
                Tax = tax,
                Discount = discount,
                Total = grandTotal,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                CouponCode = appliedCouponCode,
                CouponId = appliedCouponId,
                CouponAppliedAt = appliedCouponId != null ? DateTime.UtcNow : null,
                CouponUsageRecorded = false
            };

            var created = await _orderRepo.CreateAsync(order);

            // clear cart
            await _cartRepo.ClearAsync(userId);

            var createdResponse = ApiResponse<Order>.Created(created, "Order created successfully");
            return CreatedAtAction(nameof(Get), new { id = created.Id }, createdResponse);
        }

        // -------------------------------------------
        // PATCH: api/orders/{id}/status
        // Admin only
        // -------------------------------------------
        [HttpPatch("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<OrderStatusResponse>>> UpdateStatus(
            string id,
            [FromBody] ShoppingPlatform.DTOs.UpdateOrderStatusRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<OrderStatusResponse>.Fail("Invalid request", null, HttpStatusCode.BadRequest));

            // update status in repo
            var updated = await _orderRepo.UpdateStatusAsync(id, request.Status);
            if (!updated)
                return NotFound(ApiResponse<OrderStatusResponse>.Fail("Order not found", null, HttpStatusCode.NotFound));

            // optional: fetch order to return updated info or build response
            var order = await _orderRepo.GetByIdAsync(id);
            var responsePayload = new ShoppingPlatform.DTOs.OrderStatusResponse
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
    }
}
