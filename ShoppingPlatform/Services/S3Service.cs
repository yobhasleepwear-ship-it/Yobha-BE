using Amazon.S3;
using Amazon.S3.Model;
using ShoppingPlatform.Configurations;

namespace ShoppingPlatform.Services
{
    public interface IS3Service
    {
        string GetPreSignedUploadUrl(string bucket, string key, TimeSpan expiry);
        string GetObjectUrl(string bucket, string key);
        Task<bool> DeleteObjectAsync(string bucket, string key);
    }

    public class S3Service : IS3Service
    {
        private readonly IAmazonS3 _s3Client;

        public S3Service(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
        }

        public string GetPreSignedUploadUrl(string bucket, string key, TimeSpan expiry)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiry),
                ContentType = "image/jpeg"
            };

            return _s3Client.GetPreSignedURL(request);
        }

        public string GetObjectUrl(string bucket, string key)
        {
            return $"https://{bucket}.s3.amazonaws.com/{key}";
        }

        public async Task<bool> DeleteObjectAsync(string bucket, string key)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = key
            };

            var response = await _s3Client.DeleteObjectAsync(request);
            return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
        }
    }
}
