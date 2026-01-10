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
        public string? InvoiceUrl { get; set; }
        public string? Country { get; set; }
        public List<QuizItem> Quiz { get; set; }
        public string BuybackStatus { get; set; } = "pending"; // pending, approved, rejected
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        //New fields
        public string RequestType { get; set; } // TradeIn, RepairReuse , Recycle
        public decimal? Amount { get; set; }
        public decimal? LoyaltyPoints { get; set; }
        public string? Currency { get; set; }
        public string PaymentMethod { get; set; } = "razorpay"; // "COD" or "razorpay"
        public string PaymentStatus { get; set; } = "Pending"; // Pending, Confirmed, Paid, Failed
        public string? RazorpayOrderId { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public string? PaymentGatewayResponse { get; set; } // optional raw response
        public DeliveryDetails? deliveryDetails { get; set; } = new DeliveryDetails();


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
        public string? InvoiceUrl { get; set; }
        public string? Country { get; set; }
        public List<QuizItem> Quiz { get; set; } = new();
        public string RequestType { get; set; } // TradeIn, RepairReuse , Recycle
        public decimal? Amount { get; set; }
        public decimal? LoyaltyPoints { get; set; }
        public string? Currency { get; set; }

    }



}
