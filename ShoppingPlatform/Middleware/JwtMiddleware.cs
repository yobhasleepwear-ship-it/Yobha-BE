using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Middleware
{
    public class JwtMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly JwtService _jwt;

        public JwtMiddleware(RequestDelegate next, JwtService jwt)
        {
            _next = next;
            _jwt = jwt;
        }

        public async Task Invoke(HttpContext context)
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            var token = authHeader?.StartsWith("Bearer ") == true ? authHeader.Substring("Bearer ".Length).Trim() : authHeader;

            if (!string.IsNullOrEmpty(token))
            {
                var principal = _jwt.ValidateAccessToken(token, validateLifetime: true);
                if (principal != null)
                {
                    var uid = principal.FindFirst("uid")?.Value;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        context.Items["UserId"] = uid;
                    }
                }
            }

            await _next(context);
        }
    }
}
