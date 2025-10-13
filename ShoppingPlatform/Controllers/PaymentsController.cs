using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly IRazorpayService _razorpay;
        private readonly IOrderRepository _orderRepo;
        private readonly Services.ICouponService _couponService;

        public PaymentsController(IRazorpayService razorpay, IOrderRepository orderRepo, Services.ICouponService couponService)
        {
            _razorpay = razorpay;
            _orderRepo = orderRepo;
            _couponService = couponService;
        }

        // Client posts payment details after completing Razorpay checkout
        // { orderId, razorpayOrderId, razorpayPaymentId, razorpaySignature }
        [HttpPost("verify")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> VerifyRazorpay([FromBody] RazorpayVerifyRequest req)
        {
            if (req == null) return BadRequest(ApiResponse<object>.Fail("Invalid request", null, System.Net.HttpStatusCode.BadRequest));

            // verify signature using key secret
            var ok = _razorpay.VerifyPaymentSignature(req.razorpayOrderId, req.razorpayPaymentId, req.razorpaySignature);
            if (!ok) return BadRequest(ApiResponse<object>.Fail("Signature verification failed", null, System.Net.HttpStatusCode.BadRequest));

            // find order by our own order id (receipt)
            var order = await _orderRepo.GetByIdAsync(req.orderId);
            if (order == null) return NotFound(ApiResponse<object>.Fail("Order not found", null, System.Net.HttpStatusCode.NotFound));

            // record payment details and mark Paid
            order.RazorpayPaymentId = req.razorpayPaymentId;
            order.PaymentStatus = "Paid";
            order.Status = "Paid";
            order.PaymentGatewayResponse = $"razorpay_order:{req.razorpayOrderId}, payment:{req.razorpayPaymentId}";
            await _orderRepo.UpdateAsync(order.Id, order);

            // Mark coupon used now (if present and not already recorded)
            if (!string.IsNullOrEmpty(order.CouponId) && !order.CouponUsageRecorded)
            {
                var marked = await _couponService.MarkUsedAsync(order.CouponId!, order.UserId, order.Id);
                if (marked)
                {
                    order.CouponUsageRecorded = true;
                    await _orderRepo.UpdateAsync(order.Id, order);
                }
            }

            return Ok(ApiResponse<object>.Ok(new { orderId = order.Id, paymentId = order.RazorpayPaymentId }, "Payment verified and order updated"));
        }
    }

    public class RazorpayVerifyRequest
    {
        public string orderId { get; set; } = null!; // our db order id (receipt)
        public string razorpayOrderId { get; set; } = null!;
        public string razorpayPaymentId { get; set; } = null!;
        public string razorpaySignature { get; set; } = null!;
    }
}
