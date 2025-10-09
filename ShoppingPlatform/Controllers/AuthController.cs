using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Helpers;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using Microsoft.Extensions.Configuration;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserRepository _users;
        private readonly OtpRepository _otps;
        private readonly ISmsSender _sms;
        private readonly JwtService _jwt;
        private readonly InviteRepository _invites;
        private readonly GoogleSettings _googleSettings;
        private readonly IConfiguration _config;

        public AuthController(
            UserRepository users,
            OtpRepository otps,
            ISmsSender sms,
            JwtService jwt,
            InviteRepository invites,
            IOptions<GoogleSettings> googleOptions,
            IConfiguration config)
        {
            _users = users;
            _otps = otps;
            _sms = sms;
            _jwt = jwt;
            _invites = invites;
            _googleSettings = googleOptions.Value;
            _config = config;
        }

        // -----------------------
        // Public user registration
        // -----------------------
        [HttpPost("register-user")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> RegisterUser([FromBody] RegisterUserDto dto)
        {
            // normalize email & phone
            var email = dto.Email?.Trim().ToLowerInvariant();
            var phoneNormalized = PhoneHelper.Normalize(dto.PhoneNumber);

            // check email uniqueness
            var existingByEmail = await _users.GetByEmailAsync(email);
            if (existingByEmail is not null)
            {
                var resp = ApiResponse<string>.Fail("Email already registered", null, HttpStatusCode.Conflict);
                return Conflict(resp);
            }

            // if phone provided, check phone uniqueness
            if (!string.IsNullOrEmpty(phoneNormalized))
            {
                var existingByPhone = await _users.GetByPhoneAsync(phoneNormalized);
                if (existingByPhone is not null)
                {
                    var resp = ApiResponse<string>.Fail("Phone number already registered", null, HttpStatusCode.Conflict);
                    return Conflict(resp);
                }
            }

            // create user
            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "User" },
                FullName = dto.FullName,
                PhoneNumber = string.IsNullOrWhiteSpace(phoneNormalized) ? null : phoneNormalized,
                PhoneVerified = false,
                CreatedAt = DateTime.UtcNow,
            };

            await _users.CreateAsync(user);

            // --- auto-login: generate tokens and persist refresh token ---
            var accessToken = _jwt.GenerateToken(user);

            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // add and persist refresh token to user
            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);

            // set cookie for web clients
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName }
            };

            // Return 201 (created) along with tokens — same shape as your login response
            var body = ApiResponse<object>.Created(data, "User created and logged in");
            return CreatedAtAction(nameof(RegisterUser), new { id = user.Id }, body);
        }

        // -----------------------
        // Admin registration (Admin-only)
        // -----------------------
        [HttpPost("register-admin")]
        // [Authorize(Roles = "Admin")] // optionally keep it off during initial setup
        [AllowAnonymous] // remove this once you only want Admins to create new Admins
        public async Task<ActionResult<ApiResponse<object>>> RegisterAdmin([FromBody] RegisterAdminDto dto)
        {
            // Check if user already exists
            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
            {
                if (existing.Roles?.Contains("Admin") == true)
                {
                    var resp = ApiResponse<string>.Fail("Email already registered as Admin.", null, HttpStatusCode.Conflict);
                    return Conflict(resp);
                }

                var resp2 = ApiResponse<string>.Fail("Email already registered as User. Admin creation for existing User is not allowed.", null, HttpStatusCode.Conflict);
                return Conflict(resp2);
            }

            // Create admin user
            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "Admin" },
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            await _users.CreateAsync(user);

            // --- Auto-login: generate tokens ---
            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Save refresh token to user
            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);

            // Set cookie for refresh token
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            // Prepare response
            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName, user.Roles }
            };

            var body = ApiResponse<object>.Created(data, "Admin created and logged in");
            return CreatedAtAction(nameof(RegisterAdmin), new { id = user.Id }, body);
        }

        // -----------------------
        // Bootstrap admin - one-time creation using secret
        // -----------------------
        [HttpPost("bootstrap-admin")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> BootstrapAdmin([FromBody] BootstrapAdminDto dto)
        {
            var configured = _config["InitialAdminSecret"];
            if (string.IsNullOrEmpty(configured) || dto.Secret != configured)
            {
                var resp = ApiResponse<string>.Fail("Forbidden", null, HttpStatusCode.Forbidden);
                // Forbid() result does not accept body easily; return status with body
                return StatusCode((int)HttpStatusCode.Forbidden, resp);
            }

            var all = await _users.GetAllAsync();
            if (all.Any(u => u.Roles.Contains("Admin")))
            {
                var resp = ApiResponse<string>.Fail("Admin already exists. Bootstrap not allowed.", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
            {
                var resp = ApiResponse<string>.Fail("Email already registered.", null, HttpStatusCode.Conflict);
                return Conflict(resp);
            }

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "Admin" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);

            var body = ApiResponse<object>.Created(new { user.Id, user.Email }, "Admin created");
            return CreatedAtAction(nameof(BootstrapAdmin), new { id = user.Id }, body);
        }

        // -----------------------
        // Login (email/password)
        // -----------------------
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> Login([FromBody] LoginDto dto)
        {
            var user = await _users.GetByEmailAsync(dto.Email);
            if (user is null)
            {
                var resp = ApiResponse<string>.Fail("Invalid credentials", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            var valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!valid)
            {
                var resp = ApiResponse<string>.Fail("Invalid credentials", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // generate access token (existing behavior)
            var accessToken = _jwt.GenerateToken(user);

            // create refresh token and persist
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);

            // record IP for token (best-effort)
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);

            // set cookie (recommended for web clients) and also return token in body for mobile clients
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName }
            };
            return Ok(ApiResponse<object>.Ok(data, "Login successful"));
        }

        // -----------------------
        // Send OTP (using 2Factor)
        // -----------------------
        [HttpPost("send-otp")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> SendOtp([FromBody] SendOtpDto dto)
        {
            try
            {
                // Call the new 2Factor-based SMS service
                var sessionId = await _sms.SendOtpAsync(dto.PhoneNumber);

                // (Optional) Save sessionId for verification tracking in DB
                var entry = new OtpEntry
                {
                    PhoneNumber = dto.PhoneNumber,
                    SessionId = sessionId,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                };
                await _otps.CreateAsync(entry);

                var data = new { sessionId };
                return Ok(ApiResponse<object>.Ok(data, "OTP sent successfully"));
            }
            catch (Exception ex)
            {
                var resp = ApiResponse<string>.Fail($"Failed to send OTP: {ex.Message}", null, HttpStatusCode.InternalServerError);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }

        // -----------------------
        // Verify OTP (using 2Factor)
        // -----------------------
        [HttpPost("verify-otp")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> VerifyOtp([FromBody] VerifyOtpDto dto)
        {
            try
            {
                var entry = await _otps.GetLatestForPhoneAsync(dto.PhoneNumber);
                if (entry is null)
                {
                    var resp = ApiResponse<string>.Fail("OTP not found or expired. Request a new one.", null, HttpStatusCode.BadRequest);
                    return BadRequest(resp);
                }

                // Verify OTP with 2Factor API
                var verified = await _sms.VerifyOtpAsync(entry.SessionId!, dto.Otp);

                if (!verified)
                {
                    var resp = ApiResponse<string>.Fail("Invalid or expired OTP.", null, HttpStatusCode.Unauthorized);
                    return Unauthorized(resp);
                }

                // Mark entry used (optional)
                await _otps.MarkUsedAsync(entry.Id!);

                // Find or create user
                var user = await _users.GetByPhoneAsync(dto.PhoneNumber);
                if (user is null)
                {
                    user = new User
                    {
                        PhoneNumber = dto.PhoneNumber,
                        PhoneVerified = true,
                        Roles = new[] { "User" },
                        CreatedAt = DateTime.UtcNow
                    };
                    await _users.CreateAsync(user);
                }
                else if (!user.PhoneVerified)
                {
                    user.PhoneVerified = true;
                    await _users.UpdateAsync(user.Id!, user);
                }

                // Generate JWT and refresh token
                var accessToken = _jwt.GenerateToken(user);
                var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd2) ? rd2 : 30;
                var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
                refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                user.AddRefreshToken(refreshToken);
                user.LastLoginAt = DateTime.UtcNow;
                await _users.UpdateAsync(user.Id!, user);

                SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

                var data = new { token = accessToken, refreshToken = refreshToken.Token, user = new { user.Id, user.PhoneNumber } };

                return Ok(ApiResponse<object>.Ok(data, "OTP verified successfully"));
            }
            catch (Exception ex)
            {
                var resp = ApiResponse<string>.Fail($"OTP verification failed: {ex.Message}", null, HttpStatusCode.InternalServerError);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }


        // -----------------------
        // Google login
        // -----------------------
        [HttpPost("google")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                });
            }
            catch (Google.Apis.Auth.InvalidJwtException)
            {
                // token invalid / signature failure / expired
                var resp = ApiResponse<string>.Fail("Invalid or expired Google token", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // basic checks
            if (payload == null || string.IsNullOrEmpty(payload.Email))
            {
                var resp = ApiResponse<string>.Fail("Google token missing email", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // optional but recommended: require email verified
            if (payload.EmailVerified != true)
            {
                var resp = ApiResponse<string>.Fail("Google account email not verified", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // optional: explicit issuer check (extra safety)
            if (payload.Issuer != "accounts.google.com" && payload.Issuer != "https://accounts.google.com")
            {
                var resp = ApiResponse<string>.Fail("Invalid token issuer", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // now upsert user
            var user = await _users.GetByEmailAsync(payload.Email);

            if (user is null)
            {
                user = new User
                {
                    Email = payload.Email,
                    FullName = payload.Name,
                    Providers = new System.Collections.Generic.List<ProviderInfo> {
                        new ProviderInfo { Provider = "Google", ProviderId = payload.Subject }
                    },
                    Roles = new[] { "User" },
                    CreatedAt = DateTime.UtcNow,
                    EmailVerified = payload.EmailVerified // if you have this field on User
                };
                await _users.CreateAsync(user);
            }
            else
            {
                // ensure Providers list exists before adding
                user.Providers ??= new System.Collections.Generic.List<ProviderInfo>();

                var already = user.Providers.Exists(p => p.Provider == "Google" && p.ProviderId == payload.Subject);
                if (!already)
                {
                    user.Providers.Add(new ProviderInfo { Provider = "Google", ProviderId = payload.Subject });
                    // update email verified flag if you store it
                    if (payload.EmailVerified == true)
                        user.EmailVerified = true;

                    await _users.UpdateAsync(user.Id!, user);
                }
            }

            // generate tokens
            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd3) ? rd3 : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);

            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new { token = accessToken, refreshToken = refreshToken.Token, user = new { user.Id, user.Email, user.FullName } };
            return Ok(ApiResponse<object>.Ok(data, "Google login successful"));
        }


        // -----------------------
        // Refresh token endpoint
        // -----------------------
        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> RefreshToken([FromBody] RefreshRequest? body)
        {
            // prefer cookie, fall back to body
            var token = Request.Cookies["refreshToken"] ?? body?.RefreshToken;
            if (string.IsNullOrEmpty(token))
            {
                var resp = ApiResponse<string>.Fail("Refresh token is required", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // find user by refresh token - ensure your UserRepository implements this
            var user = await _users.GetByRefreshTokenAsync(token);
            if (user is null)
            {
                var resp = ApiResponse<string>.Fail("Invalid refresh token", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            var existing = user.GetRefreshToken(token);
            if (existing == null)
            {
                var resp = ApiResponse<string>.Fail("Refresh token not found", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            if (!existing.IsActive)
            {
                // token reuse or expired - revoke all tokens for this user as precaution
                foreach (var t in user.RefreshTokens)
                {
                    if (t.IsActive)
                        t.RevokedAt = DateTime.UtcNow;
                }
                await _users.UpdateAsync(user.Id!, user);

                var resp = ApiResponse<string>.Fail("Refresh token is no longer active", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            // rotate refresh token
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd4) ? rd4 : 30;
            var newRefresh = _jwt.GenerateRefreshToken(refreshDays);
            newRefresh.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // mark old token revoked and link replacedBy
            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            existing.ReplacedBy = newRefresh.Token;

            user.AddRefreshToken(newRefresh);
            // optional: user.PruneExpiredTokens();
            await _users.UpdateAsync(user.Id!, user);

            // new access token
            var accessToken = _jwt.GenerateToken(user);

            SetRefreshTokenCookie(newRefresh.Token, newRefresh.ExpiresAt);

            var data = new { token = accessToken, refreshToken = newRefresh.Token, expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60" };
            return Ok(ApiResponse<object>.Ok(data, "Token refreshed"));
        }


        // -----------------------
        // Revoke refresh token (logout)
        // -----------------------
        [HttpPost("revoke-refresh-token")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> RevokeRefreshToken([FromBody] RevokeRequest req)
        {
            var token = req.RefreshToken ?? Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(token))
            {
                var resp = ApiResponse<string>.Fail("Token is required", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            var user = await _users.GetByRefreshTokenAsync(token);
            if (user is null)
            {
                var resp = ApiResponse<string>.Fail("Token not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            var existing = user.GetRefreshToken(token);
            if (existing == null || !existing.IsActive)
            {
                var resp = ApiResponse<string>.Fail("Token already revoked or expired", null, HttpStatusCode.BadRequest);
                return BadRequest(resp);
            }

            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            existing.RevokeReason = "revoked-by-user";

            await _users.UpdateAsync(user.Id!, user);

            // remove cookie if present
            Response.Cookies.Delete("refreshToken");

            var ok = ApiResponse<object>.Ok(null, "Token revoked");
            return Ok(ok);
        }

        // -----------------------
        // Me - protected
        // -----------------------
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<object>>> Me()
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null)
            {
                var resp = ApiResponse<string>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            var user = await _users.GetByIdAsync(uid);
            if (user is null)
            {
                var resp = ApiResponse<string>.Fail("User not found", null, HttpStatusCode.NotFound);
                return NotFound(resp);
            }

            var data = new { user.Id, user.Email, user.FullName, user.Roles };
            return Ok(ApiResponse<object>.Ok(data));
        }

        // -----------------------
        // Helper: set refresh token cookie
        // -----------------------
        private void SetRefreshTokenCookie(string token, DateTime expiresAt)
        {
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Expires = expiresAt,
                Secure = true, // ensure HTTPS in production
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", token, cookieOptions);
        }
    }

    // -----------------------
    // DTOs for refresh/revoke (keep or move to DTOs folder)
    // -----------------------
    public class RefreshRequest
    {
        public string? RefreshToken { get; set; }
    }

    public class RevokeRequest
    {
        public string? RefreshToken { get; set; }
    }
}
