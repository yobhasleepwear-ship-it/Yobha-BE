using MongoDB.Bson;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IJobPostingRepository
    {
        Task<JobPosting> CreateAsync(JobPosting job);
        Task<JobPosting> UpdateAsync(ObjectId id, JobPosting job);
        Task<JobPosting> GetByIdAsync(ObjectId id);
        Task<JobPosting> GetByJobIdAsync(string jobId);
        Task<List<JobPosting>> GetAllAsync(string status = null);
        Task DeleteAsync(ObjectId id);
    }

}
