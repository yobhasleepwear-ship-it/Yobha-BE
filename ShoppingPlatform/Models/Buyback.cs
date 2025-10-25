using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace ShoppingPlatform.Models
{
    public class AdminUpdateBuybackRequest
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string BuybackId { get; set; }

        // Loyalty points to add (positive int)
        public int LoyaltyPoint { get; set; }

        // "Accepted" or "Rejected" - final status after inspection
        public string FinalStatus { get; set; }

        // "approve" / "reject" (initial admin action)
        public string BuybackStatus { get; set; }
    }
}
