using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Helpers;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/returns")]
    public class ReturnOrderController : ControllerBase
    {
        private readonly IReturnRepository _returnRepo;
        private readonly IOrderRepository _orderRepo;
        private readonly PaymentHelper _paymentHelper;
        private readonly ILogger<ReturnOrderController> _log;

        public ReturnOrderController(
            IReturnRepository returnRepo,
            IOrderRepository orderRepo,
            PaymentHelper paymentHelper,
            ILogger<ReturnOrderController> log)
        {
            _returnRepo = returnRepo ?? throw new ArgumentNullException(nameof(returnRepo));
            _orderRepo = orderRepo ?? throw new ArgumentNullException(nameof(orderRepo));
            _paymentHelper = paymentHelper ?? throw new ArgumentNullException(nameof(paymentHelper));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // ----------------------------
        // USER APIs
        // ----------------------------

        // POST api/returns/create
        [HttpPost("create")]
        [Authorize]
        public async Task<IActionResult> CreateReturn([FromBody] CreateReturnRequestDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            if (dto == null || string.IsNullOrWhiteSpace(dto.OrderNumber) || dto.Items == null || !dto.Items.Any())
                return BadRequest("OrderNumber and at least one item required.");

            // fetch order and ownership check
            var order = await _orderRepo.GetByOrderNumberAsync(dto.OrderNumber);
            if (order == null) return BadRequest("Order not found.");
            if (order.UserId != userId) return Forbid();

            // validate items and create snapshot of OrderItem for return
            var returnItems = new List<OrderItem>();
            foreach (var it in dto.Items)
            {
                var matched = order.Items?.FirstOrDefault(x => x.ProductId == it.ProductId || x.ProductObjectId == it.ProductObjectId);
                if (matched == null) return BadRequest($"Product {it.ProductId} not in order {dto.OrderNumber}.");

                if (it.Quantity <= 0) return BadRequest("Quantity must be > 0.");

                // Prevent requesting more than purchased quantity (simple check)
                // NOTE: Should also check previously returned qty across approved returns to avoid over-return.
                if (it.Quantity > matched.Quantity)
                    return BadRequest($"Requested return qty ({it.Quantity}) exceeds purchased qty ({matched.Quantity}) for {it.ProductId}.");

                var ri = new OrderItem
                {
                    ProductId = matched.ProductId,
                    ProductObjectId = matched.ProductObjectId,
                    ProductName = matched.ProductName,
                    Quantity = it.Quantity,
                    Size = matched.Size,
                    UnitPrice = matched.UnitPrice,
                    LineTotal = matched.UnitPrice * it.Quantity,
                    Currency = matched.Currency,
                    ThumbnailUrl = matched.ThumbnailUrl,
                    IsReturned = true,
                    ReasonForReturn = it.ReasonForReturn
                };

                returnItems.Add(ri);
            }

            var ret = new ReturnOrder
            {
                ReturnOrderNumber = GenerateReturnOrderNumber(),
                OrderNumber = dto.OrderNumber,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending",
                Items = returnItems,
                ReturnImagesURLs = dto.ReturnImagesURLs ?? new List<string>(),
                ReturnReason = dto.ReturnReason
            };

            var inserted = await _returnRepo.InsertAsync(ret);

            var response = new ReturnResponseDto
            {
                Id = inserted.Id,
                ReturnOrderNumber = inserted.ReturnOrderNumber,
                OrderNumber = inserted.OrderNumber,
                Status = inserted.Status,
                Items = inserted.Items,
                RefundAmount = inserted.RefundAmount,
                RefundStatus = inserted.RefundStatus,
                CreatedAt = inserted.CreatedAt
            };

            return Ok(response);
        }

        // GET api/returns/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMyReturns([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var list = await _returnRepo.GetByUserIdAsync(userId);
            var dto = list.Select(r => new ReturnResponseDto
            {
                Id = r.Id,
                ReturnOrderNumber = r.ReturnOrderNumber,
                OrderNumber = r.OrderNumber,
                Status = r.Status,
                Items = r.Items,
                RefundAmount = r.RefundAmount,
                RefundStatus = r.RefundStatus,
                CreatedAt = r.CreatedAt
            }).ToList();

            return Ok(dto);
        }

        // GET api/returns/{id} - accessible to owner or admin
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetReturnById(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();

            // allow owner or admin
            if (r.UserId != userId && !User.IsInRole("Admin")) return Forbid();

            var dto = new ReturnResponseDto
            {
                Id = r.Id,
                ReturnOrderNumber = r.ReturnOrderNumber,
                OrderNumber = r.OrderNumber,
                Status = r.Status,
                Items = r.Items,
                RefundAmount = r.RefundAmount,
                RefundStatus = r.RefundStatus,
                CreatedAt = r.CreatedAt
            };

            return Ok(dto);
        }

        // PUT api/returns/{id} - user can update certain fields (e.g., add images, reason) while Pending
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateReturnByUser(string id, [FromBody] UpdateReturnRequestDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();
            if (r.UserId != userId) return Forbid();
            if (r.Status != "Pending") return BadRequest("Only pending return requests can be updated by the user.");

            // allow updating images and reason only. Not items quantities here to keep stability. (Change if you want)
            if (dto.ReturnImagesURLs != null)
                r.ReturnImagesURLs = dto.ReturnImagesURLs;

            if (!string.IsNullOrWhiteSpace(dto.ReturnReason))
                r.ReturnReason = dto.ReturnReason;

            r.UpdatedAt = DateTime.UtcNow;
            await _returnRepo.UpdateAsync(r);

            return Ok(new { success = true });
        }

        // DELETE api/returns/{id} - user cancels a pending return
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> CancelReturn(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();
            if (r.UserId != userId) return Forbid();
            if (r.Status != "Pending") return BadRequest("Only pending returns can be cancelled.");

            r.Status = "Cancelled";
            r.UpdatedAt = DateTime.UtcNow;
            await _returnRepo.UpdateAsync(r);

            return Ok(new { success = true });
        }

        // ----------------------------
        // ADMIN APIs (same controller)
        // ----------------------------

        // GET api/returns/admin - list for admin with server-side filtering & paging
        [HttpGet("admin")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminList([FromQuery] string? status = null,
                                                   [FromQuery] string? orderNumber = null,
                                                   [FromQuery] int page = 1,
                                                   [FromQuery] int pageSize = 50)
        {
            // Use simple GetAllAsync and then filter & page server-side to avoid requiring AdminListAsync on repo
            var all = await _returnRepo.GetAllAsync(); // <-- add this in repository if missing
            IEnumerable<ReturnOrder> q = all;

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(orderNumber))
                q = q.Where(x => string.Equals(x.OrderNumber, orderNumber, StringComparison.OrdinalIgnoreCase));

            // server-side pagination
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);
            var paged = q.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var dto = paged.Select(r => new ReturnResponseDto
            {
                Id = r.Id,
                ReturnOrderNumber = r.ReturnOrderNumber,
                OrderNumber = r.OrderNumber,
                Status = r.Status,
                Items = r.Items,
                RefundAmount = r.RefundAmount,
                RefundStatus = r.RefundStatus,
                CreatedAt = r.CreatedAt
            }).ToList();

            return Ok(new
            {
                total = q.Count(),
                page,
                pageSize,
                items = dto
            });
        }


        // POST api/returns/admin/approve/{id}
        // Approve return and kick off refund processing (attempt refund if payment exists)
        [HttpPost("admin/approve/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminApprove(string id, [FromBody] AdminApproveDto dto)
        {
            var adminId = GetUserId();
            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();
            if (r.Status != "Pending") return BadRequest("Only pending returns can be approved.");

            var order = await _orderRepo.GetByOrderNumberAsync(r.OrderNumber);
            if (order == null) return BadRequest("Original order not found.");

            // compute refund amount (simple: sum of returned item line totals). Adjust for tax/shipping/gift rules here.
            decimal refundAmount = r.Items.Sum(i => i.LineTotal);

            // persist admin approval state BEFORE contacting external payment to avoid races
            r.Status = "Approved";
            r.AdminOverallRemarks = dto.AdminRemarks;
            r.ProcessedByAdminId = adminId;
            r.ProcessedAt = DateTime.UtcNow;
            r.RefundAmount = refundAmount;
            r.RefundStatus = "created";
            r.RefundPaymentId = order.RazorpayPaymentId;
            r.IdempotencyKey = Guid.NewGuid().ToString("N");

            await _returnRepo.UpdateAsync(r);

            // If order has no razorpay payment id -> we cannot auto-refund
            if (string.IsNullOrWhiteSpace(order.RazorpayPaymentId))
            {
                r.RefundStatus = "failed";
                r.RefundResponseRaw = "Original order has no RazorpayPaymentId; automatic refund not possible.";
                await _returnRepo.UpdateAsync(r);
                return BadRequest("Original order has no RazorpayPaymentId; cannot auto-refund.");
            }

            // Create refund
            var notes = new Dictionary<string, string>
            {
                ["returnOrderId"] = r.Id,
                ["returnOrderNumber"] = r.ReturnOrderNumber
            };

            try
            {
                var refundRes = await _paymentHelper.CreateRefundAsync(order.RazorpayPaymentId, refundAmount, dto.UseInstantRefund, notes);

                r.RefundResponseRaw = refundRes.RawResponse;
                r.RefundId = refundRes.RefundId;
                r.RefundStatus = refundRes.Success ? (refundRes.Status ?? "created") : "failed";
                r.IsProcessed = refundRes.Success;
                r.RefundCreatedAt = DateTime.UtcNow;
                await _returnRepo.UpdateAsync(r);

                if (!refundRes.Success)
                {
                    _log.LogError("Refund failed for return {ReturnId}: {Err}", r.Id, refundRes.ErrorMessage);
                    return StatusCode(500, new { success = false, error = refundRes.ErrorMessage });
                }

                // Optionally: mark order items as returned or update returned qty only after refund succeeds or after physical receive.
                // TODO: Inventory restock decision.

                return Ok(new { success = true, refundId = r.RefundId, refundStatus = r.RefundStatus });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error creating refund for return {ReturnId}", r.Id);
                r.RefundStatus = "failed";
                r.RefundResponseRaw = ex.ToString();
                await _returnRepo.UpdateAsync(r);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        // POST api/returns/admin/reject/{id}
        [HttpPost("admin/reject/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminReject(string id, [FromBody] AdminRejectDto dto)
        {
            var adminId = GetUserId();
            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();
            if (r.Status != "Pending") return BadRequest("Only pending returns can be rejected.");

            r.Status = "Rejected";
            r.AdminOverallRemarks = dto.AdminRemarks;
            r.ProcessedByAdminId = adminId;
            r.ProcessedAt = DateTime.UtcNow;
            await _returnRepo.UpdateAsync(r);

            // optionally notify user
            return Ok(new { success = true });
        }

        // Admin can also update other fields (useful for partial approvals)
        // PUT api/returns/admin/update/{id}
        [HttpPut("admin/update/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminUpdate(string id, [FromBody] AdminUpdateDto dto)
        {
            var adminId = GetUserId();
            var r = await _returnRepo.GetByIdAsync(id);
            if (r == null) return NotFound();

            // allow updating admin remarks and status if required
            if (!string.IsNullOrWhiteSpace(dto.AdminRemarks)) r.AdminOverallRemarks = dto.AdminRemarks;
            if (!string.IsNullOrWhiteSpace(dto.Status)) r.Status = dto.Status;
            r.ProcessedByAdminId = adminId;
            r.ProcessedAt = DateTime.UtcNow;
            await _returnRepo.UpdateAsync(r);

            return Ok(new { success = true });
        }

        // GET api/returns/order/{orderNumber}
        [HttpGet("order/{orderNumber}")]
        [Authorize]
        public async Task<IActionResult> GetByOrderNumber(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return BadRequest("orderNumber is required.");

            // fetch all return requests for this order
            var list = await _returnRepo.GetByOrderNumberAsync(orderNumber);

            if (list == null || !list.Any())
                return Ok(new List<ReturnResponseDto>()); // empty list

            // if user is not admin, filter to only their own return requests
            if (!User.IsInRole("Admin"))
            {
                var userId = GetUserId();
                if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

                list = list.Where(r => r.UserId == userId).ToList();
            }

            var dto = list.Select(r => new ReturnResponseDto
            {
                Id = r.Id,
                ReturnOrderNumber = r.ReturnOrderNumber,
                OrderNumber = r.OrderNumber,
                Status = r.Status,
                Items = r.Items,
                RefundAmount = r.RefundAmount,
                RefundStatus = r.RefundStatus,
                CreatedAt = r.CreatedAt
            }).ToList();

            return Ok(dto);
        }


        // ----------------------------
        // Helpers
        // ----------------------------
        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private string GenerateReturnOrderNumber()
        {
            // Example: REORD/2025/ABC123
            var seq = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
            return $"REORD/{DateTime.UtcNow.Year}/{seq}";
        }
    }
}
