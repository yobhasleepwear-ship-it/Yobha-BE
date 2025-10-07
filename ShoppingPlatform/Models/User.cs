using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string? Email { get; set; }           // optional if created by mobile
        public string? PasswordHash { get; set; }    // optional for OTP/Google users
        public string? PhoneNumber { get; set; }     // e.g. "+919876543210"
        public bool PhoneVerified { get; set; } = false;

        public bool EmailVerified { get; set; } = false;  // ✅ Added this line

        public string[] Roles { get; set; } = new[] { "User" };

        // External providers info
        public List<ProviderInfo> Providers { get; set; } = new();

        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProviderInfo
    {
        public string Provider { get; set; } = null!; // "Google", "Facebook", etc.
        public string ProviderId { get; set; } = null!; // provider-specific user id
    }
}
