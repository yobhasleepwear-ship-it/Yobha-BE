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
        private readonly ReferralService _referralService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserRepository users,
            OtpRepository otps,
            ISmsSender sms,
            JwtService jwt,
            InviteRepository invites,
            IOptions<GoogleSettings> googleOptions,
            IConfiguration config,
            ReferralService referralService,
            ILogger<AuthController> logger)
        {
            _users = users;
            _otps = otps;
            _sms = sms;
            _jwt = jwt;
            _invites = invites;
            _googleSettings = googleOptions.Value;
            _config = config;
            _referralService = referralService;
            _logger = logger;
        }

        // -----------------------
        // User registration (email/password)
        // -----------------------
        [HttpPost("register-user")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> RegisterUser([FromBody] RegisterUserDto dto)
        {
            var email = dto.Email?.Trim().ToLowerInvariant();
            var phoneNormalized = PhoneHelper.Normalize(dto.PhoneNumber);

            var existingByEmail = await _users.GetByEmailAsync(email);
            if (existingByEmail != null)
                return Conflict(ApiResponse<string>.Fail("Email already registered", null, HttpStatusCode.Conflict));

            if (!string.IsNullOrEmpty(phoneNormalized))
            {
                var existingByPhone = await _users.GetByPhoneAsync(phoneNormalized);
                if (existingByPhone != null)
                    return Conflict(ApiResponse<string>.Fail("Phone number already registered", null, HttpStatusCode.Conflict));
            }

            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "User" },
                FullName = dto.FullName,
                PhoneNumber = phoneNormalized,
                PhoneVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            await _users.CreateAsync(user);

            // Generate tokens
            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);

            // Attempt referral redemption
            try
            {
                var (redeemed, err) = await _referralService.RedeemReferralOnSignupAsync(user.Id!, email, phoneNormalized);
                if (redeemed)
                    _logger.LogInformation("Referral redeemed for new user {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral redemption failed for new user {Email}", email);
            }

            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName, LoyaltyPoints = user.LoyaltyPoints },
                role = new { roles = new[] { user.Roles } }
            };

            return CreatedAtAction(nameof(RegisterUser), new { id = user.Id },
                ApiResponse<object>.Created(data, "User created and logged in"));
        }

        // -----------------------
        // Login (email/password)
        // -----------------------
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> Login([FromBody] LoginDto dto)
        {
            var user = await _users.GetByEmailAsync(dto.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return Unauthorized(ApiResponse<string>.Fail("Invalid credentials", null, HttpStatusCode.Unauthorized));

            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName, LoyaltyPoints = user.LoyaltyPoints },
                role = new { roles = new[] { user.Roles } }
            };

            return Ok(ApiResponse<object>.Ok(data, "Login successful"));
        }

        // -----------------------
        // Verify OTP login
        // -----------------------
        [HttpPost("verify-otp")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> VerifyOtp([FromBody] VerifyOtpDto dto)
        {
            var entry = await _otps.GetLatestForPhoneAsync(dto.PhoneNumber);
            if (entry == null)
                return BadRequest(ApiResponse<string>.Fail("OTP not found or expired", null, HttpStatusCode.BadRequest));

            var verified = await _sms.VerifyOtpAsync(entry.SessionId!, dto.Otp);
            if (!verified)
                return Unauthorized(ApiResponse<string>.Fail("Invalid or expired OTP", null, HttpStatusCode.Unauthorized));

            await _otps.MarkUsedAsync(entry.Id!);

            var user = await _users.GetByPhoneAsync(dto.PhoneNumber);
            if (user == null)
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

            // Referral redemption
            try
            {
                var (redeemed, err) = await _referralService.RedeemReferralOnSignupAsync(user.Id!, user.Email, user.PhoneNumber);
                if (redeemed)
                    _logger.LogInformation("Referral redeemed on OTP signup for {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral redemption failed on OTP signup {Phone}", dto.PhoneNumber);
            }

            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.PhoneNumber, LoyaltyPoints = user.LoyaltyPoints }
            };

            return Ok(ApiResponse<object>.Ok(data, "OTP verified successfully"));
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
            catch
            {
                return Unauthorized(ApiResponse<string>.Fail("Invalid or expired Google token", null, HttpStatusCode.Unauthorized));
            }

            if (string.IsNullOrEmpty(payload.Email))
                return Unauthorized(ApiResponse<string>.Fail("Google token missing email", null, HttpStatusCode.Unauthorized));

            var user = await _users.GetByEmailAsync(payload.Email);
            var isNew = false;

            if (user == null)
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
                    EmailVerified = payload.EmailVerified
                };
                await _users.CreateAsync(user);
                isNew = true;
            }

            if (isNew)
            {
                try
                {
                    var (redeemed, err) = await _referralService.RedeemReferralOnSignupAsync(user.Id!, user.Email, user.PhoneNumber);
                    if (redeemed)
                        _logger.LogInformation("Referral redeemed on Google signup for {UserId}", user.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Referral redemption failed on Google signup for {Email}", user.Email);
                }
            }

            var accessToken = _jwt.GenerateToken(user);
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd) ? rd : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName, LoyaltyPoints = user.LoyaltyPoints }
            };

            return Ok(ApiResponse<object>.Ok(data, "Google login successful"));
        }

        // -----------------------
        // Helper
        // -----------------------
        private void SetRefreshTokenCookie(string token, DateTime expiresAt)
        {
            var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                Expires = expiresAt,
                Secure = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", token, cookieOptions);
        }
    }
}
