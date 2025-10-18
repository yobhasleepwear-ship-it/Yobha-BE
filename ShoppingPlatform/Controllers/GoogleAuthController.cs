using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
        private readonly IHostEnvironment _env;
        private readonly HashSet<string> _allowedReturnHosts;
        private readonly string? _defaultReturnUrl;

        public GoogleAuthController(
            UserRepository users,
            OtpRepository otps,
            ISmsSender sms,
            JwtService jwt,
            InviteRepository invites,
            IConfiguration config,
            IOptions<GoogleSettings> googleOptions,
            IHostEnvironment env)
        {
            _users = users;
            _otps = otps;
            _sms = sms;
            _jwt = jwt;
            _invites = invites;
            _config = config;
            _env = env;
            _googleSettings = googleOptions.Value;

            var hosts = _config.GetSection("Auth:AllowedReturnHosts").Get<string[]>() ?? Array.Empty<string>();
            _allowedReturnHosts = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);

            // IMPORTANT: set this in appsettings.json to your frontend home:
            // "Auth:DefaultReturnUrl": "https://yobha-test-env.vercel.app/home"
            _defaultReturnUrl = _config["Auth:DefaultReturnUrl"];
        }

        [HttpGet("google/redirect")]
        [AllowAnonymous]
        public IActionResult GoogleRedirect([FromQuery] string? returnUrl)
        {
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
            return Ok(new { url });
#else
            return Redirect(url);
#endif
        }

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

            // Issue JWT + refresh token
            var jwt = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);

            // Cookie options - secure in production
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Expires = refreshToken.ExpiresAt,
                Secure = !_env.IsDevelopment(),
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", refreshToken.Token, cookieOptions);

            // ---------- FORCED REDIRECT (quick fix) ----------
            // If returnUrl exists and is allowed -> redirect there.
            // Otherwise, if DefaultReturnUrl is configured, redirect there.
            // This ensures browser won't remain on backend JSON page.
            string redirectTarget = null!;
            if (!string.IsNullOrEmpty(returnUrl) && IsAllowedReturnUrl(returnUrl))
            {
                redirectTarget = returnUrl;
            }
            else if (!string.IsNullOrEmpty(_defaultReturnUrl))
            {
                redirectTarget = _defaultReturnUrl;
            }

            if (!string.IsNullOrEmpty(redirectTarget))
            {
                var separator = redirectTarget.Contains("#") ? "&" : "#";
                var redirectUrl = $"{redirectTarget}{separator}token={Uri.EscapeDataString(jwt)}";
                return Redirect(redirectUrl);
            }

            // Fallback: if no default configured, still return JSON (rare)
            return Ok(new { token = jwt, user = new { user.Id, user.Email, user.FullName }, attemptedReturnUrl = returnUrl, defaultReturnUrl = _defaultReturnUrl });
        }

        private bool IsAllowedReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl)) return false;
            if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Absolute)) return false;

            var u = new Uri(returnUrl);

            // allow localhost only on port 3000 (common dev pattern)
            if (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return u.Port == 3000;

            // Allow common Vercel pattern: any subdomain of vercel.app
            if (u.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
                return true;

            if (_allowedReturnHosts.Contains(u.Host))
                return true;

            return false;
        }
    }
}
