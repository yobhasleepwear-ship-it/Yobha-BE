using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;

namespace ShoppingPlatform.Models
{
    public class NewsletterEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? CountryCode { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class NewsletterCreateDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        // optional
        public string? CountryCode { get; set; }
        public string? PhoneNumber { get; set; }
    }
}
