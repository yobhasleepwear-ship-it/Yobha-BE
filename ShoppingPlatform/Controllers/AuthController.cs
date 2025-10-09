using System;
using System.Collections.Generic;
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
            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
            {
                var resp = ApiResponse<string>.Fail("Email already registered", null, HttpStatusCode.Conflict);
                return Conflict(resp);
            }

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "User" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);

            var body = ApiResponse<object>.Created(new { user.Id, user.Email }, "User created");
            return CreatedAtAction(nameof(RegisterUser), new { id = user.Id }, body);
        }

        // -----------------------
        // Admin registration (Admin-only)
        // -----------------------
        [HttpPost("/api/admin/register")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ApiResponse<object>>> RegisterAdmin([FromBody] RegisterAdminDto dto)
        {
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

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "Admin" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);

            var body = ApiResponse<object>.Created(new { user.Id, user.Email }, "Admin created");
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

            var token = _jwt.GenerateToken(user);
            var data = new { token, expiresInMinutes = 60, user = new { user.Id, user.Email, user.FullName } };
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

                // Generate JWT
                var token = _jwt.GenerateToken(user);
                var data = new { token, user = new { user.Id, user.PhoneNumber } };

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

            var token = _jwt.GenerateToken(user);
            var data = new { token, user = new { user.Id, user.Email, user.FullName } };
            return Ok(ApiResponse<object>.Ok(data, "Google login successful"));
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
    }
}
