namespace ShoppingPlatform.Models
{
    /// <summary>
    /// Lightweight DTO returned for pending reviews.
    /// </summary>
    public class PendingReview
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string ReviewId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
