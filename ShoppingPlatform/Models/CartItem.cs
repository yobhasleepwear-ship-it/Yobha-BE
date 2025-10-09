using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ShoppingPlatform.Models
{
    public class CartItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("userId")]
        public string UserId { get; set; } = string.Empty;

        // Readable ProductId (e.g., PID2138282)
        [BsonElement("productId")]
        public string ProductId { get; set; } = string.Empty;

        // Mongo internal product _id (ObjectId string)
        [BsonElement("productObjectId")]
        public string ProductObjectId { get; set; } = string.Empty;

        [BsonElement("productName")]
        public string ProductName { get; set; } = string.Empty;

        [BsonElement("variantSku")]
        public string VariantSku { get; set; } = string.Empty;

        [BsonElement("quantity")]
        public int Quantity { get; set; } = 1;

        [BsonElement("price")]
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; } = 0;

        [BsonElement("addedAt")]
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
