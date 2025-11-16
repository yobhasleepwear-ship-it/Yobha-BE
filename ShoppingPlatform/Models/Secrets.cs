using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class Secrets
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string? AddedFor { get; set; }
        public RazorPaySecrets? razorPaySecrets { get; set; }
        public string? SMSAPIKEY { get; set; }
    }

    public class RazorPaySecrets
    {
        public string? RAZOR_KEY_ID_INTL { get; set; }
        public string? RAZOR_KEY_ID_INR { get; set; }
        public string? RAZOR_KEY_SECRET_INTL { get; set; }
        public string? RAZOR_KEY_SECRET_INR { get; set; }

    }
}
