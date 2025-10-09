using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class OtpEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("phoneNumber")]
        public string PhoneNumber { get; set; } = string.Empty;

        [BsonElement("sessionId")]
        public string? SessionId { get; set; }

        [BsonElement("used")]
        public bool Used { get; set; } = false;

        [BsonElement("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        // 👇 Added for attempt tracking (even if 2Factor handles OTP logic)
        [BsonElement("attempts")]
        public int Attempts { get; set; } = 0;
    }
}
