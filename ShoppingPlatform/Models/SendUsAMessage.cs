using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class SendUsAMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }   
        public string Subject { get; set; }
        public string PhoneNumber { get; set; }
        public string Message { get; set; }
        public DateTime Created { get; set; }
    }
}
