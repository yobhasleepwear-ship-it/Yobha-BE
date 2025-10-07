namespace ShoppingPlatform.Configurations
{
    public class AwsS3Settings
    {
        // Backing field so both properties map to the same value
        private string _bucket = string.Empty;

        // JSON/appsettings key "Bucket" will bind to this property
        public string Bucket
        {
            get => _bucket;
            set => _bucket = value ?? string.Empty;
        }

        // Some code might be expecting "BucketName" — provide it as alias
        public string BucketName
        {
            get => _bucket;
            set => _bucket = value ?? string.Empty;
        }

        public string Region { get; set; } = "ap-south-1";

        // Optional: include keys only for local dev; avoid committing secrets
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }
}
