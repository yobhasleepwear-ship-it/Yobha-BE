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
        public int Quantity { get; set; }
        public string? Size { get; set; }
        // store money as Decimal128
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal UnitPrice { get; set; }
        public List<string>? Fabric { get; set; }
        public string? Color { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal LineTotal { get; set; } // UnitPrice * Quantity (snapshot)

        public string? Currency { get; set; }

        // Snapshot UI fields
        public string? ThumbnailUrl { get; set; }

        public bool? IsReturned { get; set; }
        public string? ReasonForReturn { get; set; }
        public string? ReturnStatus { get; set; }
        public string? AdminRemarks { get; set;  } 
        public string? Monogram { get; set; }
    }

    public class Order
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        public string OrderNumber { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public List<OrderItem>? Items { get; set; } = new();

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

        public decimal? LoyaltyDiscountAmount { get; set; }
        // coupon metadata
        public string? CouponCode { get; set; }            // e.g. "FIRST30"
        public string? CouponId { get; set; }              // coupon document _id (if validated)
        public DateTime? CouponAppliedAt { get; set; }     // when coupon was applied to this order
        public bool CouponUsageRecorded { get; set; } = false; // set true after payment success & MarkUsed called


        //Gift Card details
        public string? GiftCardNumber { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? GiftCardAmount { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? GiftCardId { get; set; }            // link to GiftCards collection (if created or used)\

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? GiftCardAppliedAmount { get; set; } // amount actually applied to this order
        public DateTime? GiftCardAppliedAt { get; set; }   // when the gift card was applied


        // Payment metadata
        public string PaymentMethod { get; set; } = "COD"; // "COD" or "razorpay"
        public string PaymentStatus { get; set; } = "Pending"; // Pending, Confirmed, Paid, Failed
        public string? RazorpayOrderId { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public string? PaymentGatewayResponse { get; set; } // optional raw response


        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        //shipping
        public string? ShippingPartner { get; set; }         // "BlueDart"
        public string? ShippingTrackingId { get; set; }      // partner tracking id
        public DateTime? ShippedAt { get; set; }
        public string? ShippingPartnerResponse { get; set; } // raw response for debugging
        public string? ShippingRemarks { get; set; }
        public string? Email { get; set; }

        public string? orderCountry { get; set; }
    }


    public class OrderFilter
    {
        public string? Id { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}
