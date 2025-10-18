namespace ShoppingPlatform.Configurations
{
    public class TwoFactorSettings
    {
        public string? ApiKey { get; set; }
        public string? SenderId { get; set; }
        public string? TemplateId { get; set; }    // keep for backward compatibility
        public string? TemplateName { get; set; }  // prefer this
        public string? DefaultVar1 { get; set; }
    }
}
