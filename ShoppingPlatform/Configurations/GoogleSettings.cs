namespace ShoppingPlatform.Configurations
{
    public class GoogleSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
        public string AuthUri { get; set; } = "https://accounts.google.com/o/oauth2/auth";
        public string UserInfoEndpoint { get; set; } = "https://www.googleapis.com/oauth2/v3/userinfo";
    }
}
