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
            if (string.IsNullOrEmpty(code))
                return BadRequest(new { message = "Missing code" });

            // decode state safely
            string? returnUrl = null;
            string? nonce = null;
            try
            {
                var stateJson = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(state));
                var stateDoc = JsonSerializer.Deserialize<JsonElement>(stateJson);
                if (stateDoc.TryGetProperty("returnUrl", out var ru)) returnUrl = ru.GetString();
                if (stateDoc.TryGetProperty("nonce", out var n)) nonce = n.GetString();
                // Optionally store nonce in server-side cache at redirect stage and validate here.
            }
            catch
            {
                // ignore decode errors for now
            }

            // Exchange code for tokens (server-side)
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

            // Validate id_token
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

            // Upsert user
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

            // Issue application JWT + refresh token if you want
            var jwt = _jwt.GenerateToken(user);

            // OPTIONAL: create and persist refresh token and set cookie (same as login)
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);

            // set HttpOnly cookie (if you want browser to get refresh cookie)
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Expires = refreshToken.ExpiresAt,
                Secure = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", refreshToken.Token, cookieOptions);

            // If a valid returnUrl is provided and whitelisted, redirect the browser to it with token in fragment
            if (!string.IsNullOrEmpty(returnUrl) && IsAllowedReturnUrl(returnUrl))
            {
                // Put token in fragment (not query) so it is not sent to servers
                var redirect = returnUrl + (returnUrl.Contains("#") ? "&" : "#") + "token=" + Uri.EscapeDataString(jwt);
                return Redirect(redirect);
            }

            // Fallback: return JSON (useful for API clients)
            return Ok(new { token = jwt, user = new { user.Id, user.Email, user.FullName }, returnUrl });
        }

        private bool IsAllowedReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl)) return false;
            // Very important: whitelist allowed frontend origins / hosts only.
            // Add your production, staging, local (ngrok) domains here.
            var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "yobha.in",
        "localhost",
        "127.0.0.1",
        // e.g. "abcd-1234.ngrok.io" for testing - add one if using ngrok
    };

            if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute)) return false;
            var u = new Uri(returnUrl);
            return allowedHosts.Contains(u.Host);
        }
    }
}
