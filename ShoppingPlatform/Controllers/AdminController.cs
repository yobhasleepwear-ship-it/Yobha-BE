using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IProductRepository _repo;
        private readonly ILogger<AdminController> _logger;
        private readonly IOrderRepository _orderRepo;
        private readonly IBuybackService _buybackRepo;
        private readonly IReturnRepository _returnRepo;


        public AdminController(IProductRepository repo, ILogger<AdminController> logger, IOrderRepository orderRepo, IBuybackService buybackRepo, IReturnRepository returnRepo)
        {
            _repo = repo;
            _logger = logger;
            _orderRepo = orderRepo;
            _buybackRepo = buybackRepo;
            _returnRepo = returnRepo;
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

        [HttpPost("ChangeOrderStatus")]
        public async Task<IActionResult> SchedulePickup(UpdateOrderStatusAdmin request)
        {

            await UpdateDeliveryStatusAsync(request);

            return Ok(new
            {
                request.id,
                request.orderStatus,
                request.paymentStatus
            });
        }

        private async Task<bool> UpdateDeliveryStatusAsync(UpdateOrderStatusAdmin request)
        {
            switch (request.type)
            {
                case "Order":
                    return await _orderRepo.UpdateDeliveryStatusAsync(request);

                case "Buyback":
                    return await _buybackRepo.UpdateDeliveryStatusAsync(request);

                case "Return":
                    return await _returnRepo.UpdateDeliveryStatusAsync(request);

                default:
                    throw new Exception("Invalid ReferenceType");
            }
        }
    }
}
