using MongoDB.Bson.Serialization.Attributes;

namespace ShoppingPlatform.Models
{
    public class Counter
    {
        [BsonId]
        public string CounterFor { get; set; } = string.Empty;

        public long Seq { get; set; } = 0;
    }
}
