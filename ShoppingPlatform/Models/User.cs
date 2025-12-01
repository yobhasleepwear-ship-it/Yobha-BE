using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ShoppingPlatform.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        // ===== Basic Identity Info =====
        public string? Email { get; set; }                // optional if created by mobile
        public string? PasswordHash { get; set; }         // optional for OTP/Google users
        public string? PhoneNumber { get; set; }          // e.g. "+919876543210"
        public bool PhoneVerified { get; set; } = false;
        public bool EmailVerified { get; set; } = false;
        public string? FullName { get; set; }

        // ===== Auth & Security =====
        [BsonElement("refreshTokens")]
        public List<RefreshToken> RefreshTokens { get; set; } = new(); // JWT refresh-token storage

        public string[] Roles { get; set; } = new[] { "User" };

        // ===== External providers (e.g., Google) =====
        public List<ProviderInfo> Providers { get; set; } = new();

        // ===== User Profile =====
        public List<Address> Addresses { get; set; } = new();

        // ===== Timestamps =====
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }

        // ===== Optional state flags =====
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? LoyaltyPoints { get; set; }
        // -------------------------
        // Helper methods for refresh tokens
        // -------------------------

        /// <summary>
        /// Add a refresh token to this user.
        /// </summary>
        public void AddRefreshToken(RefreshToken token)
        {
            if (RefreshTokens == null) RefreshTokens = new List<RefreshToken>();
            RefreshTokens.Add(token);
        }

        /// <summary>
        /// Replace an existing refresh token with a new one (rotation).
        /// Marks the old token as revoked and sets ReplacedBy on the old token.
        /// Returns true if rotation succeeded; false if old token not found / already revoked.
        /// </summary>
        public bool ReplaceRefreshToken(string oldToken, RefreshToken newToken, string? revokedByIp = null)
        {
            var existing = RefreshTokens?.FirstOrDefault(t => t.Token == oldToken);
            if (existing == null || !existing.IsActive) return false;

            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedByIp = revokedByIp;
            existing.ReplacedBy = newToken.Token;

            AddRefreshToken(newToken);
            return true;
        }

        public bool RevokeRefreshToken(string token, string? revokedByIp = null, string? reason = null)
        {
            var existing = RefreshTokens?.FirstOrDefault(t => t.Token == token);
            if (existing == null || !existing.IsActive) return false;

            existing.RevokedAt = DateTime.UtcNow;
            existing.RevokedByIp = revokedByIp;
            existing.RevokeReason = reason;
            return true;
        }


        /// <summary>
        /// Find a refresh token if present (null otherwise).
        /// </summary>
        public RefreshToken? GetRefreshToken(string token)
        {
            return RefreshTokens?.FirstOrDefault(t => t.Token == token);
        }

        /// <summary>
        /// Remove expired tokens older than a grace window (optional).
        /// </summary>
        public void PruneExpiredTokens(TimeSpan? olderThan = null)
        {
            if (RefreshTokens == null) return;
            if (olderThan == null)
            {
                // Default: remove tokens that are expired for more than 30 days
                olderThan = TimeSpan.FromDays(30);
            }

            var cutoff = DateTime.UtcNow - olderThan.Value;
            RefreshTokens = RefreshTokens
                .Where(t => !(t.IsExpired && (t.RevokedAt == null || t.RevokedAt < cutoff)))
                .ToList();
        }

        // ---------
        // Static helper to generate cryptographically-strong refresh token strings
        // ---------
        public static string GenerateRefreshTokenString(int byteLength = 64)
        {
            var bytes = new byte[byteLength];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            // Use Base64Url (replace + / =), but here we return hex for simplicity:
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // ===== External Provider Info =====
    public class ProviderInfo
    {
        public string Provider { get; set; } = null!; // e.g. "Google", "Facebook"
        public string ProviderId { get; set; } = null!; // provider-specific user id
    }

    // ===== Refresh Token Model =====
    public class RefreshToken
    {
        public RefreshToken()
        {
            Token = User.GenerateRefreshTokenString();
            IssuedAt = DateTime.UtcNow;
            // Default expiry 30 days - you can override when creating
            ExpiresAt = DateTime.UtcNow.AddDays(30);
            // Don't assign to computed properties. Ensure RevokedAt is null by default.
            RevokedAt = null;
        }

        /// <summary>
        /// The actual token string stored server-side.
        /// </summary>
        public string Token { get; set; } = null!;

        /// <summary>
        /// When the token was issued (UTC)
        /// </summary>
        public DateTime IssuedAt { get; set; }

        /// <summary>
        /// When the token expires (UTC)
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// If token was revoked (manually/rotation), timestamp (UTC)
        /// </summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>
        /// IP address which requested revocation or rotation
        /// </summary>
        public string? RevokedByIp { get; set; }

        /// <summary>
        /// Optional reason for revocation (audit)
        /// </summary>
        public string? RevokeReason { get; set; }

        /// <summary>
        /// If this token was replaced by another token (rotation), store the replacement token string
        /// </summary>
        public string? ReplacedBy { get; set; }

        /// <summary>
        /// Optional: store IP that created this token (for audit)
        /// </summary>
        public string? CreatedByIp { get; set; }

        /// <summary>
        /// True if token was explicitly revoked (RevokedAt set)
        /// </summary>
        [BsonIgnore]
        public bool IsRevoked => RevokedAt != null;

        /// <summary>
        /// True if token is expired
        /// </summary>
        [BsonIgnore]
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        /// <summary>
        /// Active means not revoked and not expired
        /// </summary>
        [BsonIgnore]
        public bool IsActive => !IsRevoked && !IsExpired;
    }


    // ===== Address Model =====
    public class Address
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Line1 { get; set; } = null!;
        public string? Line2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Zip { get; set; } = null!;
        public string Country { get; set; } = null!;
        public bool IsDefault { get; set; } = false;
        public string MobileNumner { get; set; } = null!;
    }
}
