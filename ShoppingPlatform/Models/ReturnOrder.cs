using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class ReturnOrder
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        public string ReturnOrderNumber { get; set; } = string.Empty; // REORD/YYYY/0001
        public string OrderNumber { get; set; } = string.Empty;       // original order number
        public string UserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected
        public List<OrderItem> Items { get; set; } = new();
        public string? AdminOverallRemarks { get; set; }
        public bool IsProcessed { get; set; } = false; // whether refund/stock done
        public List<string> ReturnImagesURLs { get; set; } = new();
        public string ReturnReason { get; set; }
    }
}
