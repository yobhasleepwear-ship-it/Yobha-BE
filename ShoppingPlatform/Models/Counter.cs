using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class Counter
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string CounterFor { get; set; } = string.Empty;

        public long Seq { get; set; } = 0;
    }
}
