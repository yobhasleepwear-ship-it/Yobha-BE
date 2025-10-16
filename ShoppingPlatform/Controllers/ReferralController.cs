using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.Models;
using ShoppingPlatform.Services;
using System.Collections.Generic;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReferralController : ControllerBase
    {
        private readonly ReferralService _referralService;

        public ReferralController(ReferralService referralService)
        {
            _referralService = referralService;
        }

        /// <summary>
        /// Create a referral for an email or phone. Authenticated users only.
        /// Request: { "email": "...", "phone": "..." }  (one of them required)
        /// </summary>
        [HttpPost("create")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> CreateReferral([FromBody] CreateReferralRequest req)
        {
            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("uid")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(ApiResponse<object>.Fail("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));

            if (req == null || (string.IsNullOrWhiteSpace(req.Email) && string.IsNullOrWhiteSpace(req.Phone)))
                return BadRequest(ApiResponse<object>.Fail("Provide referred email or phone", null, System.Net.HttpStatusCode.BadRequest));

            var (ok, err) = await _referralService.CreateReferralAsync(userId, req.Email, req.Phone);
            if (!ok)
                return BadRequest(ApiResponse<object>.Fail(err ?? "Failed to create referral", null, System.Net.HttpStatusCode.BadRequest));

            return Ok(ApiResponse<object>.Ok(null, "Referral created"));
        }

        /// <summary>
        /// Get referrals created by the current user.
        /// </summary>
        [HttpGet("mine")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Referral>>>> MyReferrals()
        {
            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("uid")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(ApiResponse<List<Referral>>.Fail("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));

            var list = await _referralService.GetReferralsByReferrerAsync(userId);
            return Ok(ApiResponse<List<Referral>>.Ok(list, "OK"));
        }
    }

    public class CreateReferralRequest
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }
}
