using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class OtpEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string PhoneNumber { get; set; } = null!;
        public string OtpHash { get; set; } = null!;    // hashed OTP
        public string Salt { get; set; } = null!;       // salt for hashing
        public DateTime ExpiresAt { get; set; }
        public int Attempts { get; set; } = 0;
        public bool Used { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
