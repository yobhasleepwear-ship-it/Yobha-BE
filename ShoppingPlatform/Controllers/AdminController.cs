using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IProductRepository _repo;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IProductRepository repo, ILogger<AdminController> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        [HttpPost("MakeAdmin/{email}")]
        public async Task<IActionResult> MakeAdmin(string email, [FromServices] UserRepository users)
        {
            var user = await users.GetByEmailAsync(email);
            if (user is null) return NotFound();

            user.Roles = user.Roles.Concat(new[] { "Admin" }).Distinct().ToArray();
            await users.UpdateAsync(user.Id!, user);
            return Ok(user);
        }

        [HttpPost("seed/products")]
        public async Task<IActionResult> SeedProducts([FromQuery] int count = 10)
        {
            if (count <= 0 || count > 500) return BadRequest("count must be 1..500");

            var rnd = new Random();
            var created = new List<Product>();

            var categories = new[] { "electronics", "books", "clothing", "home", "toys" };

            for (int i = 0; i < count; i++)
            {
                var p = new Product
                {
                    Id = null!,
                    Name = $"Demo Product {Guid.NewGuid().ToString().Substring(0, 8)}",
                    Slug = $"demo-product-{Guid.NewGuid().ToString().Substring(0, 8)}",
                    Description = "Auto-seeded demo product.",
                    Price = (decimal)(rnd.NextDouble() * 1000 + 10),
                    Category = categories[rnd.Next(categories.Length)],
                    Stock = rnd.Next(0, 200),
                    IsFeatured = rnd.Next(0, 10) == 0,
                    SalesCount = rnd.Next(0, 500),
                    Images = new List<ProductImage>()
                };

                await _repo.CreateAsync(p);
                created.Add(p);
            }

            return Ok(new { seeded = created.Count });
        }
    }
}
