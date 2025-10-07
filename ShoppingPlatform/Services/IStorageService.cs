namespace ShoppingPlatform.Services
{
    public interface IStorageService
    {
        /// <summary>Returns a presigned URL (PUT) and the final public file URL.</summary>
        Task<(string uploadUrl, string fileUrl)> GeneratePreSignedUploadUrlAsync(string key, TimeSpan expiresIn);
    }

}
