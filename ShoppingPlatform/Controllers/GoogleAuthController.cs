using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Google.Apis.Auth;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GoogleAuthController : ControllerBase
    {
        private readonly UserRepository _users;
        private readonly OtpRepository _otps;
        private readonly ISmsSender _sms;
        private readonly JwtService _jwt;
        private readonly InviteRepository _invites;
        private readonly GoogleSettings _googleSettings;
        private readonly IConfiguration _config;

        public GoogleAuthController(
            UserRepository users,
            OtpRepository otps,
            ISmsSender sms,
            JwtService jwt,
            InviteRepository invites,
            IConfiguration config,
            IOptions<GoogleSettings> googleOptions)
        {
            _users = users;
            _otps = otps;
            _sms = sms;
            _jwt = jwt;
            _invites = invites;
            _config = config;
            _googleSettings = googleOptions.Value;
        }

        /// <summary>
        /// Start Google OAuth 2.0 authorization - redirects browser to Google consent screen.
        /// Note: open this URL in a browser tab (Swagger "Execute" won't follow redirects).
        /// </summary>
        [HttpGet("google/redirect")]
        [AllowAnonymous]
        public IActionResult GoogleRedirect([FromQuery] string? returnUrl)
        {
            // Build state containing nonce + returnUrl
            var stateObj = new { nonce = Guid.NewGuid().ToString("N"), returnUrl };
            var stateJson = JsonSerializer.Serialize(stateObj);
            var state = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(stateJson));

            var query = new Dictionary<string, string?>
            {
                ["client_id"] = _googleSettings.ClientId,
                ["redirect_uri"] = _googleSettings.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["state"] = state
            };

            var authUri = _googleSettings.AuthUri?.TrimEnd('/') ?? "https://accounts.google.com/o/oauth2/v2/auth";
            var url = QueryHelpers.AddQueryString(authUri, query!);

#if DEBUG
            // In dev return the URL for easy testing in Swagger (copy/paste)
            return Ok(new { url });
#else
    // In production immediately redirect the browser to Google
    return Redirect(url);
#endif
        }


        /// <summary>
        /// Callback endpoint Google will call with ?code=...&state=...
        /// Exchanges the code for tokens, validates id_token, upserts user and returns app JWT.
        /// </summary>
        [HttpGet("google/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleCallback([FromQuery] string code, [FromQuery] string state)
        {
            if (string.IsNullOrEmpty(code)) return BadRequest(new { message = "Missing code" });

            string? returnUrl = null;
            try
            {
                var stateJson = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(state));
                var s = JsonSerializer.Deserialize<JsonElement>(stateJson);
                if (s.TryGetProperty("returnUrl", out var ru)) returnUrl = ru.GetString();
            }
            catch { /* ignore decode errors for now */ }

            // Exchange code for tokens (your existing code)
            using var http = new HttpClient();
            var form = new Dictionary<string, string?>
            {
                ["code"] = code,
                ["client_id"] = _googleSettings.ClientId,
                ["client_secret"] = _googleSettings.ClientSecret,
                ["redirect_uri"] = _googleSettings.RedirectUri,
                ["grant_type"] = "authorization_code"
            };

            var tokenResponse = await http.PostAsync(_googleSettings.TokenUri, new FormUrlEncodedContent(form!));
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var err = await tokenResponse.Content.ReadAsStringAsync();
                return BadRequest(new { message = "Token exchange failed", details = err });
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var tokenDoc = JsonSerializer.Deserialize<JsonElement>(tokenJson);
            if (!tokenDoc.TryGetProperty("id_token", out var idTokenElem))
                return BadRequest(new { message = "id_token not returned by Google" });

            var idToken = idTokenElem.GetString();

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "Invalid id_token", detail = ex.Message });
            }

            // Upsert user (your existing logic)
            var user = await _users.GetByEmailAsync(payload.Email);
            if (user == null)
            {
                user = new User
                {
                    Email = payload.Email,
                    FullName = payload.Name,
                    EmailVerified = payload.EmailVerified,
                    Providers = new List<ProviderInfo> { new ProviderInfo { Provider = "Google", ProviderId = payload.Subject } },
                    Roles = new[] { "User" },
                    CreatedAt = DateTime.UtcNow
                };
                await _users.CreateAsync(user);
            }
            else
            {
                user.Providers ??= new List<ProviderInfo>();
                if (!user.Providers.Exists(p => p.Provider == "Google" && p.ProviderId == payload.Subject))
                {
                    user.Providers.Add(new ProviderInfo { Provider = "Google", ProviderId = payload.Subject });
                    if (payload.EmailVerified == true) user.EmailVerified = true;
                    await _users.UpdateAsync(user.Id!, user);
                }
            }

            // Issue JWT
            var jwt = _jwt.GenerateToken(user);

            // OPTIONAL: create refresh token and set cookie (remember dev cookie caveat below)
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);

            // Set refresh cookie — in production keep Secure = true.
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Expires = refreshToken.ExpiresAt,
                Secure = true, // NOTE: if frontend is http://localhost:3000, Secure=true prevents browser from sending the cookie in development
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", refreshToken.Token, cookieOptions);

            // Redirect the browser to frontend returnUrl (if whitelisted),
            // placing token in the fragment so it isn't sent to servers.
            if (!string.IsNullOrEmpty(returnUrl) && IsAllowedReturnUrl(returnUrl))
            {
                // Example result: http://localhost:3000/home#token=<jwt>
                var separator = returnUrl.Contains("#") ? "&" : "#";
                var redirectUrl = $"{returnUrl}{separator}token={Uri.EscapeDataString(jwt)}";
                return Redirect(redirectUrl);
            }

            // Fallback: return JSON (API clients)
            return Ok(new { token = jwt, user = new { user.Id, user.Email, user.FullName } });
        }

        private bool IsAllowedReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl)) return false;
            if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute)) return false;

            var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "yobha.in",
        "localhost",
        "127.0.0.1"
        // you can add ngrok host e.g. "abcd-1234.ngrok.io"
    };

            var u = new Uri(returnUrl);
            // allow specific localhost ports (common dev pattern)
            if (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return u.Port == 3000; // accept only localhost:3000

            return allowedHosts.Contains(u.Host);
        }
    }
}
