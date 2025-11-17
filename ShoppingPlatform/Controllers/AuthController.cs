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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

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
        private readonly ISmsGatewayService _smsGatewayService;

        public AuthController(
            UserRepository users,
            OtpRepository otps,
            ISmsSender sms,
            JwtService jwt,
            InviteRepository invites,
            IOptions<GoogleSettings> googleOptions,
            IConfiguration config,
            ReferralService referralService,
            ILogger<AuthController> logger,
            ISmsGatewayService smsGatewayService)
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
            _smsGatewayService = smsGatewayService;
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

            user.AddRefreshToken(refreshToken);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user);

            // Attempt referral redemption (best-effort)
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

            // set cookie for web clients
            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new
            {
                token = accessToken,
                expiresInMinutes = _config["Jwt:ExpiryMinutes"] ?? "60",
                refreshToken = refreshToken.Token,
                user = new { user.Id, user.Email, user.FullName, LoyaltyPoints = user.LoyaltyPoints },
                role = new { roles = new[] { user.Roles } }
            };

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
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
            };

            await _users.CreateAsync(user);

            // --- Auto-login: generate tokens ---
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
                FullName = dto.FullName,
                CreatedAt = DateTime.UtcNow
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
            var user = await _users.GetByEmailAsync(dto.Email);//GetByIdAsync
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

            // generate access token
            var accessToken = _jwt.GenerateToken(user);

            // create refresh token and persist
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
        // Send OTP (using 2Factor)
        // -----------------------
        [HttpPost("send-otp")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> SendOtp([FromBody] SendOtpDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.PhoneNumber))
            {
                return BadRequest(ApiResponse<object>.Fail(
                    "Invalid request",
                    new List<string> { "phoneNumber is required" },
                    HttpStatusCode.BadRequest));
            }

            _logger.LogInformation("API /send-otp called for phone={phoneMask}", MaskPhone(dto.PhoneNumber));

            try
            {
                // call provider (this returns providerResult and the plain otp generated by the service)
                var (providerResult, otp) = await _smsGatewayService.SendOtpAsync(dto.PhoneNumber);

                // persist entry (store hashed OTP)
                var entry = new OtpEntry
                {
                    PhoneNumber = dto.PhoneNumber,
                    SessionId = providerResult.SessionId,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5), // configurable
                    OtpHash = OtpHashHelper.HashOtp(otp),
                    ProviderStatus = providerResult.ProviderStatus,
                    ProviderMessageId = providerResult.ProviderMessageId,
                    ProviderRawResponse = providerResult.RawResponse,
                    AttemptCount = 0
                };

                await _otps.CreateAsync(entry); // your repository method

                _logger.LogInformation("SendOtp result for phone={phoneMask} success={ok} status={status} session={sid}",
                    MaskPhone(dto.PhoneNumber), providerResult.IsSuccess, providerResult.ProviderStatus, providerResult.SessionId);

                var data = new
                {
                    sessionId = providerResult.SessionId,
                    providerStatus = providerResult.ProviderStatus,
                    providerMessageId = providerResult.ProviderMessageId
                    // DO NOT return OTP in API response
                };

                if (!providerResult.IsSuccess)
                {
                    _logger.LogWarning("Provider rejected SMS for {phoneMask} status={status} raw={raw}",
                        MaskPhone(dto.PhoneNumber),
                        providerResult.ProviderStatus,
                        Truncate(providerResult.RawResponse, 1000));

                    return StatusCode((int)HttpStatusCode.BadGateway, ApiResponse<object>.Fail(
                        "Provider rejected SMS",
                        new List<string> {
                    providerResult.ProviderStatus,
                    Truncate(providerResult.RawResponse, 1000)
                        },
                        HttpStatusCode.BadGateway));
                }

                return Ok(ApiResponse<object>.Ok(data, "OTP sent successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendOtp failed for phone {phoneMask}", MaskPhone(dto?.PhoneNumber));

                return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail(
                    "Failed to send OTP",
                    new List<string> { ex.Message },
                    HttpStatusCode.InternalServerError));
            }
        }
        //public async Task<ActionResult<ApiResponse<object>>> SendOtp([FromBody] SendOtpDto dto)
        //{
        //    if (dto == null || string.IsNullOrWhiteSpace(dto.PhoneNumber))
        //    {
        //        return BadRequest(ApiResponse<object>.Fail(
        //            "Invalid request",
        //            new List<string> { "phoneNumber is required" },
        //            HttpStatusCode.BadRequest));
        //    }

        //    _logger.LogInformation("API /send-otp called for phone={phoneMask}", MaskPhone(dto.PhoneNumber));

        //    try
        //    {
        //        var providerResult = await _sms.SendOtpAsync(dto.PhoneNumber);

        //        // persist entry
        //        var entry = new OtpEntry
        //        {
        //            PhoneNumber = dto.PhoneNumber,
        //            SessionId = providerResult.SessionId,
        //            CreatedAt = DateTime.UtcNow,
        //            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        //        };
        //        await _otps.CreateAsync(entry);

        //        _logger.LogInformation("SendOtp result for phone={phoneMask} success={ok} status={status} session={sid}",
        //            MaskPhone(dto.PhoneNumber), providerResult.IsSuccess, providerResult.ProviderStatus, providerResult.SessionId);

        //        var data = new
        //        {
        //            sessionId = providerResult.SessionId,
        //            providerStatus = providerResult.ProviderStatus,
        //            providerMessageId = providerResult.ProviderMessageId,
        //            providerRaw = providerResult.RawResponse
        //        };

        //        if (!providerResult.IsSuccess)
        //        {
        //            _logger.LogWarning("Provider rejected SMS for {phoneMask} status={status} raw={raw}",
        //                MaskPhone(dto.PhoneNumber),
        //                providerResult.ProviderStatus,
        //                Truncate(providerResult.RawResponse, 1000));

        //            return StatusCode((int)HttpStatusCode.BadGateway, ApiResponse<object>.Fail(
        //                "Provider rejected SMS",
        //                new List<string> {
        //            providerResult.ProviderStatus,
        //            Truncate(providerResult.RawResponse, 1000)
        //                },
        //                HttpStatusCode.BadGateway));
        //        }

        //        return Ok(ApiResponse<object>.Ok(data, "OTP sent successfully"));
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "SendOtp failed for phone {phoneMask}", MaskPhone(dto?.PhoneNumber));

        //        return StatusCode((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail(
        //            "Failed to send OTP",
        //            new List<string> { ex.Message },
        //            HttpStatusCode.InternalServerError));
        //    }
        //}

        private static string MaskPhone(string? phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var digits = Regex.Replace(phone, @"\D", "");
            return digits.Length <= 4 ? new string('*', digits.Length) : new string('*', digits.Length - 4) + digits[^4..];
        }

        private static string Truncate(string? s, int n) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s.Substring(0, n) + "...");


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

                var verified = await _sms.VerifyOtpAsync(entry.SessionId!, dto.Otp);
                if (!verified)
                {
                    var resp = ApiResponse<string>.Fail("Invalid or expired OTP.", null, HttpStatusCode.Unauthorized);
                    return Unauthorized(resp);
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

                // token generation
                var accessToken = _jwt.GenerateToken(user);
                var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd2) ? rd2 : 30;
                var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
                refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                user.AddRefreshToken(refreshToken);
                user.LastLoginAt = DateTime.UtcNow;
                await _users.UpdateAsync(user.Id!, user);

                SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

                var data = new { token = accessToken, refreshToken = refreshToken.Token, user = new { user.Id, user.PhoneNumber, LoyaltyPoints = user.LoyaltyPoints } };

                return Ok(ApiResponse<object>.Ok(data, "OTP verified successfully"));
            }
            catch (Exception ex)
            {
                var resp = ApiResponse<string>.Fail($"OTP verification failed: {ex.Message}", null, HttpStatusCode.InternalServerError);
                return StatusCode((int)HttpStatusCode.InternalServerError, resp);
            }
        }
        //public async Task<ActionResult<ApiResponse<object>>> VerifyOtp([FromBody] VerifyOtpDto dto)
        //{
        //    try
        //    {
        //        var entry = await _otps.GetLatestForPhoneAsync(dto.PhoneNumber);
        //        if (entry is null)
        //        {
        //            var resp = ApiResponse<string>.Fail("OTP not found or expired. Request a new one.", null, HttpStatusCode.BadRequest);
        //            return BadRequest(resp);
        //        }

        //        var verified = await _sms.VerifyOtpAsync(entry.SessionId!, dto.Otp);
        //        if (!verified)
        //        {
        //            var resp = ApiResponse<string>.Fail("Invalid or expired OTP.", null, HttpStatusCode.Unauthorized);
        //            return Unauthorized(resp);
        //        }

        //        await _otps.MarkUsedAsync(entry.Id!);

        //        var user = await _users.GetByPhoneAsync(dto.PhoneNumber);
        //        if (user is null)
        //        {
        //            user = new User
        //            {
        //                PhoneNumber = dto.PhoneNumber,
        //                PhoneVerified = true,
        //                Roles = new[] { "User" },
        //                CreatedAt = DateTime.UtcNow
        //            };
        //            await _users.CreateAsync(user);
        //        }
        //        else if (!user.PhoneVerified)
        //        {
        //            user.PhoneVerified = true;
        //            await _users.UpdateAsync(user.Id!, user);
        //        }

        //        // Referral redemption
        //        try
        //        {
        //            var (redeemed, err) = await _referralService.RedeemReferralOnSignupAsync(user.Id!, user.Email, user.PhoneNumber);
        //            if (redeemed)
        //                _logger.LogInformation("Referral redeemed on OTP signup for {UserId}", user.Id);
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError(ex, "Referral redemption failed on OTP signup {Phone}", dto.PhoneNumber);
        //        }

        //        // token generation
        //        var accessToken = _jwt.GenerateToken(user);
        //        var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd2) ? rd2 : 30;
        //        var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
        //        refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        //        user.AddRefreshToken(refreshToken);
        //        user.LastLoginAt = DateTime.UtcNow;
        //        await _users.UpdateAsync(user.Id!, user);

        //        SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

        //        var data = new { token = accessToken, refreshToken = refreshToken.Token, user = new { user.Id, user.PhoneNumber, LoyaltyPoints = user.LoyaltyPoints } };

        //        return Ok(ApiResponse<object>.Ok(data, "OTP verified successfully"));
        //    }
        //    catch (Exception ex)
        //    {
        //        var resp = ApiResponse<string>.Fail($"OTP verification failed: {ex.Message}", null, HttpStatusCode.InternalServerError);
        //        return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        //    }
        //}

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
                var resp = ApiResponse<string>.Fail("Invalid or expired Google token", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google token validation error");
                return Unauthorized(ApiResponse<string>.Fail("Invalid Google token", null, HttpStatusCode.Unauthorized));
            }

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
            else
            {
                user.Providers ??= new System.Collections.Generic.List<ProviderInfo>();
                var already = user.Providers.Exists(p => p.Provider == "Google" && p.ProviderId == payload.Subject);
                if (!already)
                {
                    user.Providers.Add(new ProviderInfo { Provider = "Google", ProviderId = payload.Subject });
                    if (payload.EmailVerified == true)
                        user.EmailVerified = true;
                    await _users.UpdateAsync(user.Id!, user);
                }
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
            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd3) ? rd3 : 30;
            var refreshToken = _jwt.GenerateRefreshToken(refreshDays);
            refreshToken.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            user.AddRefreshToken(refreshToken);
            await _users.UpdateAsync(user.Id!, user);

            SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

            var data = new { token = accessToken, refreshToken = refreshToken.Token, user = new { user.Id, user.Email, user.FullName, LoyaltyPoints = user.LoyaltyPoints } };
            return Ok(ApiResponse<object>.Ok(data, "Google login successful"));
        }

        // -----------------------
        // Refresh token endpoint
        // -----------------------
        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> RefreshToken([FromBody] RefreshRequest? body)
        {
            var token = Request.Cookies["refreshToken"] ?? body?.RefreshToken;
            if (string.IsNullOrEmpty(token))
            {
                var resp = ApiResponse<string>.Fail("Refresh token is required", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

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
                // revoke all as precaution
                foreach (var t in user.RefreshTokens)
                {
                    if (t.IsActive)
                        t.RevokedAt = DateTime.UtcNow;
                }
                await _users.UpdateAsync(user.Id!, user);

                var resp = ApiResponse<string>.Fail("Refresh token is no longer active", null, HttpStatusCode.Unauthorized);
                return Unauthorized(resp);
            }

            var refreshDays = int.TryParse(_config["Jwt:RefreshDays"], out var rd4) ? rd4 : 30;
            var newRefresh = _jwt.GenerateRefreshToken(refreshDays);
            newRefresh.CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            existing.ReplacedBy = newRefresh.Token;

            user.AddRefreshToken(newRefresh);
            await _users.UpdateAsync(user.Id!, user);

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

            var data = new { user.Id, user.Email, user.FullName, user.Roles, LoyaltyPoints = user.LoyaltyPoints };
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

        // -----------------------
        // Addresses: list/add/update/delete (protected)
        // -----------------------
        [HttpGet("addresses")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Address>>>> GetAddresses()
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null)
                return Unauthorized(ApiResponse<List<Address>>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            var addresses = await _users.GetAddressesAsync(uid);
            return Ok(ApiResponse<List<Address>>.Ok(addresses));
        }

        [HttpPost("addresses")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Address>>>> AddAddress([FromBody] CreateAddressDto dto)
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null)
                return Unauthorized(ApiResponse<List<Address>>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            if (dto == null)
                return BadRequest(ApiResponse<List<Address>>.Fail("Invalid request", null, HttpStatusCode.BadRequest));

            var address = new Address
            {
                Id = string.IsNullOrEmpty(dto.Id) ? null : dto.Id,
                FullName = dto.FullName,
                Line1 = dto.Line1,
                Line2 = dto.Line2,
                City = dto.City,
                State = dto.State,
                Zip = dto.Zip,
                Country = dto.Country,
                IsDefault = dto.IsDefault,
                MobileNumner = dto.MobileNumnber
            };

            await _users.AddAddressAsync(uid, address);

            var addresses = await _users.GetAddressesAsync(uid);
            return Created("", ApiResponse<List<Address>>.Created(addresses, "Address added"));
        }

        [HttpPut("addresses/{addressId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Address>>>> UpdateAddress(string addressId, [FromBody] UpdateAddressDto dto)
        {
            var uid = User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value;
            if (uid is null)
                return Unauthorized(ApiResponse<List<Address>>.Fail("Unauthorized - user id claim not found", null, System.Net.HttpStatusCode.Unauthorized));

            if (dto == null)
                return BadRequest(ApiResponse<List<Address>>.Fail("Invalid request", null, System.Net.HttpStatusCode.BadRequest));

            var address = new Address
            {
                Id = addressId,
                FullName = dto.FullName,
                Line1 = dto.Line1,
                Line2 = dto.Line2,
                City = dto.City,
                State = dto.State,
                Zip = dto.Zip,
                Country = dto.Country,
                IsDefault = dto.IsDefault,
                MobileNumner = dto.MobileNumnber
            };

            var updated = await _users.UpdateAddressAsync(uid, address);
            if (!updated)
                return NotFound(ApiResponse<List<Address>>.Fail("Address not found or not owned by user", null, System.Net.HttpStatusCode.NotFound));

            var addresses = await _users.GetAddressesAsync(uid);
            return Ok(ApiResponse<List<Address>>.Ok(addresses, "Address updated"));
        }

        [HttpDelete("addresses/{addressId}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<List<Address>>>> DeleteAddress(string addressId)
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null)
                return Unauthorized(ApiResponse<List<Address>>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            var ok = await _users.RemoveAddressAsync(uid, addressId);
            if (!ok) return NotFound(ApiResponse<List<Address>>.Fail("Address not found", null, HttpStatusCode.NotFound));

            var addresses = await _users.GetAddressesAsync(uid);
            return Ok(ApiResponse<List<Address>>.Ok(addresses, "Address removed"));
        }

        // -----------------------
        // Update user full name
        // -----------------------
        [HttpPatch("name")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<User>>> UpdateName([FromBody] UpdateNameDto dto)
        {
            var uid = User.FindFirst("uid")?.Value;
            if (uid is null)
                return Unauthorized(ApiResponse<User>.Fail("Unauthorized", null, HttpStatusCode.Unauthorized));

            if (dto == null || string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(ApiResponse<User>.Fail("fullName is required", null, HttpStatusCode.BadRequest));

            var ok = await _users.UpdateUserNameAsync(uid, dto.FullName.Trim());
            if (!ok) return NotFound(ApiResponse<User>.Fail("User not found", null, HttpStatusCode.NotFound));

            var user = await _users.GetByIdAsync(uid);
            return Ok(ApiResponse<User>.Ok(user!, "Name updated"));
        }

    }

    // -----------------------
    // DTOs for refresh/revoke
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
