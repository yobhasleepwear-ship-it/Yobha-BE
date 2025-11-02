using MongoDB.Bson;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IJobPostingRepository
    {
        Task<JobPosting> CreateAsync(JobPosting job);
        Task<JobPosting> UpdateAsync(string id, JobPosting job);
        Task<JobPosting> GetByIdAsync(string id);
        Task<JobPosting> GetByJobIdAsync(string jobId);
        Task<List<JobPosting>> GetAllAsync(string status = null);
        Task DeleteAsync(string id);
    }

}
