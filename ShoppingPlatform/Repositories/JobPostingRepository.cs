using MongoDB.Bson;
using MongoDB.Driver;

namespace ShoppingPlatform.Repositories
{
    public class JobPostingRepository : IJobPostingRepository
    {
        private readonly IMongoCollection<JobPosting> _col;


        public JobPostingRepository(IMongoDatabase db, string collectionName = "jobPostings")
        {
            _col = db.GetCollection<JobPosting>(collectionName);
            // ensure unique index on jobId
            var idx = new CreateIndexModel<JobPosting>(Builders<JobPosting>.IndexKeys.Ascending(j => j.JobId), new CreateIndexOptions { Unique = true });
            _col.Indexes.CreateOne(idx);
        }


        public async Task<JobPosting> CreateAsync(JobPosting job)
        {
            // enforce uniqueness at application level to provide friendlier error
            var existing = await _col.Find(j => j.JobId == job.JobId).FirstOrDefaultAsync();
            if (existing != null) throw new System.Exception($"JobId '{job.JobId}' already exists.");


            job.Id = ObjectId.GenerateNewId().ToString();
            job.CreatedAt = DateTime.UtcNow;
            job.UpdatedAt = job.CreatedAt;
            await _col.InsertOneAsync(job);
            return job;
        }


        public async Task<JobPosting> UpdateAsync(string id, JobPosting job)
        {
            // If job.JobId changed, ensure uniqueness
            var existingWithJobId = await _col.Find(j => j.JobId == job.JobId && j.Id != id).FirstOrDefaultAsync();
            if (existingWithJobId != null) throw new System.Exception($"JobId '{job.JobId}' is already in use by another posting.");


            job.UpdatedAt = DateTime.UtcNow;
            var res = await _col.ReplaceOneAsync(j => j.Id == id, job);
            if (res.MatchedCount == 0) return null;
            return job;
        }


        public async Task<JobPosting> GetByIdAsync(string id)
        {
            return await _col.Find(j => j.Id == id).FirstOrDefaultAsync();
        }


        public async Task<JobPosting> GetByJobIdAsync(string jobId)
        {
            return await _col.Find(j => j.JobId == jobId).FirstOrDefaultAsync();
        }


        public async Task<List<JobPosting>> GetAllAsync(string status = null)
        {
            var filter = status == null ? Builders<JobPosting>.Filter.Empty : Builders<JobPosting>.Filter.Eq(j => j.Status, status);
            return await _col.Find(filter).ToListAsync();
        }


        public async Task DeleteAsync(string id)
        {
            await _col.DeleteOneAsync(j => j.Id == id);
        }
    }
}
