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

        public OrdersController(IOrderRepository orderRepo, ICartRepository cartRepo, IProductRepository productRepo)
        {
            _orderRepo = orderRepo;
            _cartRepo = cartRepo;
            _productRepo = productRepo;
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
        public async Task<ActionResult<ApiResponse<Order>>> Create()
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous";

            // Get the user's cart items (raw persisted CartItem model)
            var cartItems = await _cartRepo.GetForUserAsync(userId);
            if (cartItems == null || !cartItems.Any())
            {
                var badResponse = ApiResponse<Order>.Fail("Cart is empty", null, HttpStatusCode.BadRequest);
                return BadRequest(badResponse);
            }

            var orderItems = new List<OrderItem>();
            decimal subTotal = 0m;
            string currency = "INR";

            foreach (var cartItem in cartItems)
            {
                // Prefer using the snapshot stored inside the cart item
                var snap = cartItem.Snapshot;

                // If snapshot is missing for some reason, try to hydrate from product repo
                if (snap == null || string.IsNullOrWhiteSpace(snap.ProductId))
                {
                    // fallback: try to fetch product by Mongo _id (ProductObjectId) or ProductId
                    ShoppingPlatform.Models.Product? product = null;
                    if (!string.IsNullOrWhiteSpace(cartItem.ProductObjectId))
                        product = await _productRepo.GetByIdAsync(cartItem.ProductObjectId);
                    if (product == null && !string.IsNullOrWhiteSpace(cartItem.ProductId))
                        product = await _productRepo.GetByProductIdAsync(cartItem.ProductId);

                    if (product == null) continue; // skip missing product

                    // find variant if any
                    ProductVariant? variant = null;
                    if (!string.IsNullOrWhiteSpace(cartItem.VariantSku) && product.Variants != null)
                        variant = product.Variants.FirstOrDefault(v => v.Sku == cartItem.VariantSku);

                    decimal unitPrice = variant?.PriceOverride ?? product.Price;

                    var oiFallback = new OrderItem
                    {
                        ProductId = product.ProductId,
                        ProductObjectId = product.Id,
                        ProductName = product.Name,
                        VariantSku = cartItem.VariantSku,
                        VariantId = variant?.Id,
                        Quantity = cartItem.Quantity,
                        UnitPrice = unitPrice,
                        LineTotal = unitPrice * cartItem.Quantity,
                        CompareAtPrice = product.CompareAtPrice,
                        Currency = "INR",
                        ThumbnailUrl = variant?.Images?.FirstOrDefault()?.Url ?? product.Images?.FirstOrDefault()?.Url,
                        Slug = product.Slug
                    };

                    orderItems.Add(oiFallback);
                    subTotal += oiFallback.LineTotal;
                    currency = oiFallback.Currency ?? currency;
                }
                else
                {
                    // use snapshot values (trust snapshot as the canonical price at add-to-cart)
                    decimal unitPrice = snap.UnitPrice;
                    var oi = new OrderItem
                    {
                        ProductId = snap.ProductId,
                        ProductObjectId = snap.ProductObjectId,
                        ProductName = snap.Name,
                        VariantSku = snap.VariantSku ?? cartItem.VariantSku,
                        VariantId = snap.VariantId,
                        Quantity = cartItem.Quantity,
                        UnitPrice = unitPrice,
                        LineTotal = unitPrice * cartItem.Quantity,
                        CompareAtPrice = snap.CompareAtPrice,
                        Currency = snap.Currency ?? "INR",
                        ThumbnailUrl = snap.ThumbnailUrl,
                        Slug = snap.Slug
                    };

                    orderItems.Add(oi);
                    subTotal += oi.LineTotal;
                    currency = oi.Currency; // assume currency consistent across items; if mixed, handle conversions
                }
            }

            if (!orderItems.Any())
            {
                var badResponse = ApiResponse<Order>.Fail("No valid items in cart to create order", null, HttpStatusCode.BadRequest);
                return BadRequest(badResponse);
            }

            // compute totals: subTotal already computed
            decimal shipping = 0m; // compute shipping rules here if any
            decimal tax = 0m;      // compute tax if needed
            decimal discount = 0m; // apply coupon/discount if any
            decimal grandTotal = Decimal.Round(subTotal + shipping + tax - discount, 2);

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
                CreatedAt = DateTime.UtcNow
            };

            var created = await _orderRepo.CreateAsync(order);

            // Optionally: clear cart after order creation
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
