using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Services;

public class S3StorageService : IStorageService
{
    private readonly AmazonS3Client _s3;
    private readonly AwsS3Settings _settings;

    public S3StorageService(IOptions<AwsS3Settings> options)
    {
        _settings = options.Value;
        var region = RegionEndpoint.GetBySystemName(_settings.Region);
        // You can let SDK pick up AWS credentials from env or use explicit credentials:
        if (!string.IsNullOrEmpty(_settings.AccessKey) && !string.IsNullOrEmpty(_settings.SecretKey))
        {
            _s3 = new AmazonS3Client(_settings.AccessKey, _settings.SecretKey, region);
        }
        else
        {
            // fallback to default credential chain (env, IAM role, shared creds)
            _s3 = new AmazonS3Client(region);
        }
    }

    public Task<(string uploadUrl, string fileUrl)> GeneratePreSignedUploadUrlAsync(string key, TimeSpan expiresIn)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiresIn),
            ContentType = "application/octet-stream"
            // optionally set ContentType constraint
        };

        var url = _s3.GetPreSignedURL(request);
        var fileUrl = $"https://{_settings.BucketName}.s3.{_settings.Region}.amazonaws.com/{key}";
        return Task.FromResult((url, fileUrl));
    }
}
