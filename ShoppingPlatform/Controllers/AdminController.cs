using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        public async Task<ActionResult<ApiResponse<object>>> MakeAdmin(string email, [FromServices] UserRepository users)
        {
            var user = await users.GetByEmailAsync(email);
            if (user is null)
            {
                var resp = ApiResponse<string>.Fail("User not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            user.Roles = user.Roles.Concat(new[] { "Admin" }).Distinct().ToArray();
            await users.UpdateAsync(user.Id!, user);

            var data = new { user.Id, user.Email, user.Roles };
            var success = ApiResponse<object>.Ok(data, "User promoted to Admin");
            return Ok(success);
        }

        [HttpPost("seed/products")]
        public async Task<ActionResult<ApiResponse<object>>> SeedProducts([FromQuery] int count = 10)
        {
            if (count <= 0 || count > 500)
            {
                var resp = ApiResponse<string>.Fail("count must be between 1 and 500", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

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

            var result = new { seeded = created.Count };
            var ok = ApiResponse<object>.Ok(result, "Products seeded");
            return Ok(ok);
        }
    }
}
