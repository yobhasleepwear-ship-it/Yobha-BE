using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class OtpEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        // store hashed OTP (do NOT store plain OTP)
        public string OtpHash { get; set; } = string.Empty;

        // verification metadata
        public bool IsVerified { get; set; } = false;
        public DateTime? VerifiedAt { get; set; }

        // Provider diagnostic fields (optional but useful)
        public string? ProviderStatus { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ProviderRawResponse { get; set; }

        public int AttemptCount { get; set; } = 0;
        public int MaxAttempts { get; set; } = 5;

        // free text field
        public string? Note { get; set; }
    }

}
