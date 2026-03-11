namespace ShoppingPlatform.Configurations
{
    public class BrevoSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.brevo.com/v3";

        public bool Enabled { get; set; } = false;

        // Optional list mapping by event.
        public int? SignupListId { get; set; }
        public int? OrderPlacedListId { get; set; }
        public int? CartAbandonedListId { get; set; }

        // Cart-abandon detection config.
        public int CartAbandonAfterMinutes { get; set; } = 60;
        public int CartAbandonScanIntervalMinutes { get; set; } = 15;
    }
}
