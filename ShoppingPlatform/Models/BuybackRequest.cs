using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class BuybackRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string UserId { get; set; } // Authenticated user's Id

        public string? OrderId { get; set; } // Null if external marketplace
        public string ProductId { get; set; }

        public List<string> ProductUrl { get; set; }
        public string InvoiceUrl { get; set; }
        public string? Country { get; set; }

        public List<QuizItem> Quiz { get; set; }

        public string BuybackStatus { get; set; } = "pending"; // pending, approved, rejected
        public string FinalStatus { get; set; } = "pending";   // accepted, rejected
        public string DeliveryStatus { get; set; } = "pending"; // inTransit, received
        // Mock pickup fields (previously missing)
        public string PickupTrackingId { get; set; }
        public DateTime? PickupScheduledAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class QuizItem
    {
        public string Ques { get; set; }
        public string Ans { get; set; }
    }

    public class CreateBuybackDto
    {
        public string? OrderId { get; set; }
        public string ProductId { get; set; }
        public List<string> ProductUrl { get; set; }
        public string InvoiceUrl { get; set; }
        public string? Country { get; set; }
        public List<QuizItem> Quiz { get; set; } = new();
    }

}
