using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class CartAbandonAudit
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public DateTime LastCartUpdatedAt { get; set; }
        public DateTime LastNotifiedAt { get; set; }
    }
}
