namespace ShoppingPlatform.Configurations
{
    public class JwtSettings
    {
        public string Key { get; set; } = null!;
        public string Issuer { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public int ExpiryMinutes { get; set; } = 15;      // access token lifetime
        public int RefreshDays { get; set; } = 30;        // refresh token lifetime (days)
        public int ClockSkewSeconds { get; set; } = 60;   // token validation clock skew
    }
}
