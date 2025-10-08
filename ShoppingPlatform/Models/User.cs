using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

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
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
        public bool Revoked { get; set; } = false;
        public string? ReplacedBy { get; set; } // for token rotation tracking
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
    }
}
