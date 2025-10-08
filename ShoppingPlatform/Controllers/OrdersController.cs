using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Models;

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
            return Ok(new ApiResponse<IEnumerable<Order>> { Success = true, Message = "OK", Data = list });
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
                return NotFound(new ApiResponse<Order> { Success = false, Message = "Order not found" });

            return Ok(new ApiResponse<Order> { Success = true, Message = "OK", Data = order });
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
                return BadRequest(new ApiResponse<Order> { Success = false, Message = "Cart is empty" });

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

            return CreatedAtAction(nameof(Get), new { id = created.Id }, new ApiResponse<Order>
            {
                Success = true,
                Message = "Order created successfully",
                Data = created
            });
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
                return NotFound(new ApiResponse<object> { Success = false, Message = "Order not found" });

            return Ok(new ApiResponse<object> { Success = true, Message = "Status updated" });
        }
    }
}
