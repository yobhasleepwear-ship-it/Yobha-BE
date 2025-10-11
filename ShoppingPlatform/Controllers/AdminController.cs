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

    }
}
