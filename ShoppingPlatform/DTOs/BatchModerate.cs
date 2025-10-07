namespace ShoppingPlatform.DTOs
{
    public class BatchModerationRequest
    {
        public string Action { get; set; } = string.Empty; // "approve" or "reject"
        public List<ModerationItem> Items { get; set; } = new();
    }

    public class ModerationItem
    {
        public string ProductId { get; set; } = string.Empty;
        public string ReviewId { get; set; } = string.Empty;
    }
}
