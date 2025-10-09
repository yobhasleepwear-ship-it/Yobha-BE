using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

            // Get the user's cart
            var cartItems = await _cartRepo.GetForUserAsync(userId);
            if (!cartItems.Any())
            {
                var badResponse = ApiResponse<Order>.Fail("Cart is empty", null, HttpStatusCode.BadRequest);
                return BadRequest(badResponse);
            }

            // Build order items with price snapshot
            var orderItems = new List<OrderItem>();
            decimal total = 0;

            foreach (var item in cartItems)
            {
                var product = await _productRepo.GetByIdAsync(item.ProductId);
                if (product == null) continue;

                // Determine price from product or variant
                decimal unitPrice = product.Price;
                if (product.Variants != null && product.Variants.Any(v => v.Sku == item.VariantSku))
                {
                    var variant = product.Variants.First(v => v.Sku == item.VariantSku);
                    if (variant.PriceOverride.HasValue)
                        unitPrice = variant.PriceOverride.Value;
                }

                orderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = product.Name,
                    VariantSku = item.VariantSku,
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice
                });

                total += unitPrice * item.Quantity;
            }

            // Create order object
            var order = new Order
            {
                UserId = userId,
                Items = orderItems,
                Total = total,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            var created = await _orderRepo.CreateAsync(order);

            // Optionally clear the cart
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
        public async Task<ActionResult<ApiResponse<object>>> UpdateStatus(string id, [FromBody] string status)
        {
            var updated = await _orderRepo.UpdateStatusAsync(id, status);
            if (!updated)
            {
                var notFoundResponse = ApiResponse<object>.Fail("Order not found", null, HttpStatusCode.NotFound);
                return NotFound(notFoundResponse);
            }

            var response = ApiResponse<object>.Ok(null, "Status updated successfully");
            return Ok(response);
        }
    }
}
