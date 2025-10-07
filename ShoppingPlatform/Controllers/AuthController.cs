using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Google.Apis.Auth;
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
        public async Task<IActionResult> RegisterUser([FromBody] RegisterUserDto dto)
        {
            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
                return Conflict(new { message = "Email already registered" });

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "User" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);
            return CreatedAtAction(nameof(RegisterUser), new { id = user.Id }, new { user.Id, user.Email });
        }

        // -----------------------
        // Admin registration (Admin-only)
        // -----------------------
        [HttpPost("/api/admin/register")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminDto dto)
        {
            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
            {
                if (existing.Roles?.Contains("Admin") == true)
                    return Conflict(new { message = "Email already registered as Admin." });

                return Conflict(new { message = "Email already registered as User. Admin creation for existing User is not allowed." });
            }

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "Admin" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);
            return CreatedAtAction(nameof(RegisterAdmin), new { id = user.Id }, new { user.Id, user.Email });
        }

        // -----------------------
        // Bootstrap admin - one-time creation using secret
        // -----------------------
        [HttpPost("bootstrap-admin")]
        [AllowAnonymous]
        public async Task<IActionResult> BootstrapAdmin([FromBody] BootstrapAdminDto dto)
        {
            var configured = _config["InitialAdminSecret"];
            if (string.IsNullOrEmpty(configured) || dto.Secret != configured)
                return Forbid();

            var all = await _users.GetAllAsync();
            if (all.Any(u => u.Roles.Contains("Admin")))
                return BadRequest(new { message = "Admin already exists. Bootstrap not allowed." });

            var existing = await _users.GetByEmailAsync(dto.Email);
            if (existing is not null)
                return Conflict(new { message = "Email already registered." });

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Roles = new[] { "Admin" },
                FullName = dto.FullName
            };

            await _users.CreateAsync(user);
            return CreatedAtAction(nameof(BootstrapAdmin), new { id = user.Id }, new { user.Id, user.Email });
        }

        // -----------------------
        // Login (email/password)
        // -----------------------
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _users.GetByEmailAsync(dto.Email);
            if (user is null) return Unauthorized(new { message = "Invalid credentials" });

            var valid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!valid) return Unauthorized(new { message = "Invalid credentials" });

            var token = _jwt.GenerateToken(user);
            return Ok(new { token, expiresInMinutes = 60, user = new { user.Id, user.Email, user.FullName } });
        }

        // -----------------------
        // Send OTP
        // -----------------------
        [HttpPost("send-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
        {
            var last = await _otps.GetLatestForPhoneAsync(dto.PhoneNumber);
            if (last != null && last.CreatedAt.AddSeconds(60) > DateTime.UtcNow)
                return BadRequest(new { message = "OTP recently sent. Try again later." });

            var otp = OtpHelper.GenerateNumericOtp(6);
            var tuple = OtpHelper.HashOtp(otp);
            var hash = tuple.hashed;
            var salt = tuple.salt;

            var entry = new OtpEntry
            {
                PhoneNumber = dto.PhoneNumber,
                OtpHash = hash,
                Salt = salt,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            };

            await _otps.CreateAsync(entry);

            var message = $"Your verification code is {otp}. It will expire in 5 minutes.";
            await _sms.SendSmsAsync(dto.PhoneNumber, message);

            return Ok(new { message = "OTP sent" });
        }

        // -----------------------
        // Verify OTP
        // -----------------------
        [HttpPost("verify-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
        {
            var entry = await _otps.GetLatestForPhoneAsync(dto.PhoneNumber);
            if (entry is null) return BadRequest(new { message = "No OTP found. Request a new one." });
            if (entry.Used) return BadRequest(new { message = "OTP already used." });
            if (entry.ExpiresAt < DateTime.UtcNow) return BadRequest(new { message = "OTP expired." });
            if (entry.Attempts >= 5) return BadRequest(new { message = "Too many attempts." });

            if (!OtpHelper.VerifyOtp(dto.Otp, entry.Salt, entry.OtpHash))
            {
                entry.Attempts++;
                await _otps.IncrementAttemptsAsync(entry.Id!, entry.Attempts);
                return Unauthorized(new { message = "Invalid OTP" });
            }

            await _otps.MarkUsedAsync(entry.Id!);

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

            var token = _jwt.GenerateToken(user);
            return Ok(new { token, user = new { user.Id, user.PhoneNumber } });
        }

        // -----------------------
        // Google login
        // -----------------------
        [HttpPost("google")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                });
            }
            catch (Exception)
            {
                return BadRequest(new { message = "Invalid Google token" });
            }

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
                    CreatedAt = DateTime.UtcNow
                };
                await _users.CreateAsync(user);
            }
            else
            {
                var already = user.Providers?.Exists(p => p.Provider == "Google" && p.ProviderId == payload.Subject) ?? false;
                if (!already)
                {
                    user.Providers.Add(new ProviderInfo { Provider = "Google", ProviderId = payload.Subject });
                    await _users.UpdateAsync(user.Id!, user);
                }
            }

            var token = _jwt.GenerateToken(user);
            return Ok(new { token, user = new { user.Id, user.Email, user.FullName } });
        }

        // -----------------------
        // Me - protected
        // -----------------------
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null) return Unauthorized();

            var user = await _users.GetByIdAsync(uid);
            if (user is null) return NotFound();

            return Ok(new { user.Id, user.Email, user.FullName, user.Roles });
        }
    }
}
