using MongoDB.Bson;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public interface IApplicantRepository
    {
        Task<Applicant> CreateAsync(Applicant applicant);
        Task<List<Applicant>> GetByJobIdAsync(string jobId, int skip = 0, int limit = 50);
        Task<List<Applicant>> GetAllAsync(string jobTitleFilter = null, int skip = 0, int limit = 100);
        Task<Applicant> GetByIdAsync(string id);
        Task UpdateStatusAsync(string applicantId, string status);
    }
}
