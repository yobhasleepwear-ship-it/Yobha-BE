// File: Models/OrderModels.cs
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Models
{
    public class OrderItem
    {
        // readable product id (PID...)
        public string ProductId { get; set; } = string.Empty;

        // mongo product _id (object id string) — useful for lookups
        public string ProductObjectId { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;
        public string VariantSku { get; set; } = string.Empty;
        public string? VariantId { get; set; }
        public int Quantity { get; set; }

        // store money as Decimal128
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal UnitPrice { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal LineTotal { get; set; } // UnitPrice * Quantity (snapshot)

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? CompareAtPrice { get; set; }

        public string Currency { get; set; } = "INR";

        // Snapshot UI fields
        public string? ThumbnailUrl { get; set; }
        public string? Slug { get; set; }
    }

    public class Order
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;
        public List<OrderItem> Items { get; set; } = new();

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal SubTotal { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Shipping { get; set; } = 0m;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Tax { get; set; } = 0m;

        // Discount applied (derived from coupon or manual discount)
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Discount { get; set; } = 0m;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Total { get; set; }
        public string Currency { get; set; }

        public Address ShippingAddress { get; set; }

        // coupon metadata
        public string? CouponCode { get; set; }            // e.g. "FIRST30"
        public string? CouponId { get; set; }              // coupon document _id (if validated)
        public DateTime? CouponAppliedAt { get; set; }     // when coupon was applied to this order
        public bool CouponUsageRecorded { get; set; } = false; // set true after payment success & MarkUsed called

        // Payment metadata
        public string PaymentMethod { get; set; } = "COD"; // "COD" or "razorpay"
        public string PaymentStatus { get; set; } = "Pending"; // Pending, Confirmed, Paid, Failed
        public string? RazorpayOrderId { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public string? PaymentGatewayResponse { get; set; } // optional raw response


        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }


    public class OrderFilter
    {
        public string? Id { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}
