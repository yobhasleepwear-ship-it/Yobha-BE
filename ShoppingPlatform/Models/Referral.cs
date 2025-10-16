using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class Referral
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        // User who created the referral (referrer)
        public string ReferrerUserId { get; set; } = null!;

        // The email/phone being referred (one of them should be present)
        public string? ReferredEmail { get; set; }
        public string? ReferredPhone { get; set; }

        // Redeem tracking
        public bool IsRedeemed { get; set; } = false;
        public string? ReferredUserId { get; set; } // set when referred person signs up
        public DateTime? RedeemedAt { get; set; }

        // created time
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
