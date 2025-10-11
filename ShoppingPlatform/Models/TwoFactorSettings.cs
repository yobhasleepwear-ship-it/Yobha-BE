namespace ShoppingPlatform.Configurations
{
    public class TwoFactorSettings
    {
        public string? ApiKey { get; set; }
        public string? SenderId { get; set; }
        public string? TemplateName { get; set; }    // optional: e.g. "OTPSendTemplate1"
        public string? DefaultVar1 { get; set; }     // optional: default name placeholder
    }
}
