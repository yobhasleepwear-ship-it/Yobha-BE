using System;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
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
        private readonly IAmazonS3 _s3;
        private readonly AwsS3Settings _aws;

        public S3Service(IAmazonS3 s3Client, IOptions<AwsS3Settings> awsOptions)
        {
            _s3 = s3Client;
            _aws = awsOptions.Value;
        }

        public string GetPreSignedUploadUrl(string bucket, string key, TimeSpan expiry)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiry),
                ContentType = "application/octet-stream"
            };
            return _s3.GetPreSignedURL(request);
        }

        public string GetObjectUrl(string bucket, string key)
        {
            return $"https://{bucket}.s3.{_aws.Region}.amazonaws.com/{key}";
        }

        public async Task<bool> DeleteObjectAsync(string bucket, string key)
        {
            if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(key))
                return false;

            try
            {
                var request = new DeleteObjectRequest
                {
                    BucketName = bucket,
                    Key = key
                };
                var response = await _s3.DeleteObjectAsync(request);
                // Successful delete returns 204 NoContent but SDK returns response with HttpStatusCode
                return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent ||
                       response.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (AmazonS3Exception)
            {
                // log if you have logger; swallow and return false so DB isn't altered if delete failed
                return false;
            }
        }
    }
}
