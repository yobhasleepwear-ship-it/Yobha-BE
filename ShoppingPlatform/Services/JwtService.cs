using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Models;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ShoppingPlatform.Services
{
    public class JwtService
    {
        private readonly JwtSettings _settings;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly JwtSecurityTokenHandler _handler = new JwtSecurityTokenHandler();

        public JwtService(IOptions<JwtSettings> options)
        {
            _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key ?? string.Empty));
        }

        // ---------- Existing method preserved (exact behavior) ----------
        // This keeps your current code working where GenerateToken(user) was used.
        public string GenerateToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim("uid", user.Id ?? string.Empty)
            };

            // add role claims
            if (user.Roles != null)
            {
                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
                signingCredentials: creds
            );

            return _handler.WriteToken(token);
        }

        // ---------- Backwards-compatible wrapper (optional if other code expects GenerateAccessToken) ----------
        // Keep it for future usage / clarity
        public string GenerateAccessToken(User user, IEnumerable<Claim>? extraClaims = null)
        {
            var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim("uid", user.Id ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (extraClaims != null) claims.AddRange(extraClaims);

            if (user.Roles != null)
            {
                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(_settings.ExpiryMinutes),
                signingCredentials: creds
            );

            return _handler.WriteToken(token);
        }

        // ---------- Generate an opaque refresh token model ----------
        public RefreshToken GenerateRefreshToken(int? days = null)
        {
            var ttlDays = days ?? (_settings.RefreshDays > 0 ? _settings.RefreshDays : 30);
            return new RefreshToken
            {
                Token = Guid.NewGuid().ToString("N"),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(ttlDays),
                Revoked = false,
                ReplacedBy = null
            };
        }

        // ---------- Validate access token and return ClaimsPrincipal (or null if invalid) ----------
        /// <summary>
        /// Validate the provided JWT access token. If validateLifetime is false, expiry will NOT be enforced
        /// (useful when you want to validate signature to extract claims during refresh flows).
        /// </summary>
        public ClaimsPrincipal? ValidateAccessToken(string token, bool validateLifetime = true)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            try
            {
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _signingKey,
                    ValidateLifetime = validateLifetime,
                    ClockSkew = TimeSpan.FromSeconds(_settings.ClockSkewSeconds > 0 ? _settings.ClockSkewSeconds : 60)
                };

                var principal = _handler.ValidateToken(token, parameters, out var validatedToken);

                if (validatedToken is JwtSecurityToken jwt && jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
                {
                    return principal;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
