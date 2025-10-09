namespace ShoppingPlatform.Configurations
{
    public class TwoFactorSettings
    {
        public string? ApiKey { get; set; }
        public string? SenderId { get; set; }
        public string? BaseUrl { get; set; } = "https://2factor.in";
    }
}
