using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace ShoppingPlatform.Models
{
    public enum CouponType
    {
        Percentage,
        Fixed
    }

    public class Coupon
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string Code { get; set; } = null!;         // e.g., "FIRST100", unique (uppercase)
        [BsonRepresentation(BsonType.String)]
        public CouponType Type { get; set; }              // Percentage or Fixed
        public decimal Value { get; set; }                // 30 for 30% or 100 for ₹100 off

        // Optional constraints
        public decimal? MinOrderAmount { get; set; }      // minimum cart amount to be eligible
        public decimal? MaxDiscountAmount { get; set; }   // cap for percentage discounts

        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }

        // Limits
        public int? GlobalUsageLimit { get; set; }        // how many times coupon can be used overall (e.g., 100)
        public int UsedCount { get; set; } = 0;

        // Per-user control & tracking
        /// <summary>how many times a single user can use this coupon (1 => one-time per user)</summary>
        public int? PerUserUsageLimit { get; set; } = 1;

        /// <summary>list of userIds who have used the coupon (used to prevent reuse)</summary>
        public List<string>? UsedByUserIds { get; set; } = new();

        // Convenience flags
        public bool IsActive { get; set; } = true;
        public bool FirstOrderOnly { get; set; } = false;

        // Optional: applicability by product/category - kept simple for now
        public List<string>? ApplicableProductIds { get; set; }
        public List<string>? ApplicableCategories { get; set; }

        // Metadata
        public string? CreatedByUserId { get; set; }      // admin who created it
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = string.Empty;

    }
}
