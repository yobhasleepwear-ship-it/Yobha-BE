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
            // In dev return the URL so Swagger can show it (copy-paste into new tab)
            return Ok(new { url });
#else
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

            // Decode state and optionally read returnUrl (you should also validate state/nonce in production)
            string? returnUrl = null;
            try
            {
                var stateJson = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(state));
                var stateObj = JsonSerializer.Deserialize<JsonElement>(stateJson);
                if (stateObj.TryGetProperty("returnUrl", out var r)) returnUrl = r.GetString();
            }
            catch
            {
                // ignore state decode errors for now (but validate in production)
            }

            // Exchange code for tokens
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
            var accessToken = tokenDoc.TryGetProperty("access_token", out var at) ? at.GetString() : null;

            // Validate id_token payload
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                });
            }
            catch (InvalidJwtException ex)
            {
                return Unauthorized(new { message = "Invalid id_token", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "id_token validation failed", detail = ex.Message });
            }

            // Upsert user and issue your JWT
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

                var already = user.Providers.Exists(p => p.Provider == "Google" && p.ProviderId == payload.Subject);
                if (!already)
                {
                    user.Providers.Add(new ProviderInfo { Provider = "Google", ProviderId = payload.Subject });
                    // update email verified flag if present
                    if (payload.EmailVerified == true)
                        user.EmailVerified = true;

                    await _users.UpdateAsync(user.Id!, user);
                }
            }

            var jwt = _jwt.GenerateToken(user);

            // Return JSON (or set cookie + redirect if you prefer)
            return Ok(new { token = jwt, user = new { user.Id, user.Email, user.FullName }, returnUrl });
        }
    }
}
