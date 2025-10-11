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

        // Provider diagnostic fields (optional but useful)
        public string? ProviderStatus { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ProviderRawResponse { get; set; }

        // free text field
        public string? Note { get; set; }
    }

}
