using MongoDB.Bson;
using MongoDB.Driver;

namespace ShoppingPlatform.Repositories
{
    public class ApplicantRepository : IApplicantRepository
    {
        private readonly IMongoCollection<Applicant> _col;


        public ApplicantRepository(IMongoDatabase db, string collectionName = "applicants")
        {
            _col = db.GetCollection<Applicant>(collectionName);
            _col.Indexes.CreateOne(new CreateIndexModel<Applicant>(Builders<Applicant>.IndexKeys.Ascending(a => a.JobId)));
            _col.Indexes.CreateOne(new CreateIndexModel<Applicant>(Builders<Applicant>.IndexKeys.Ascending(a => a.JobTitle)));
            _col.Indexes.CreateOne(new CreateIndexModel<Applicant>(Builders<Applicant>.IndexKeys.Ascending(a => a.Email)));
        }


        public async Task<Applicant> CreateAsync(Applicant applicant)
        {
            applicant.Id = ObjectId.GenerateNewId();
            applicant.AppliedAt = DateTime.UtcNow;
            await _col.InsertOneAsync(applicant);
            return applicant;
        }


        public async Task<List<Applicant>> GetByJobIdAsync(string jobId, int skip = 0, int limit = 50)
        {
            return await _col.Find(a => a.JobId == jobId).Skip(skip).Limit(limit).ToListAsync();
        }


        public async Task<List<Applicant>> GetAllAsync(string jobTitleFilter = null, int skip = 0, int limit = 100)
        {
            var filter = Builders<Applicant>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(jobTitleFilter))
            {
                filter = Builders<Applicant>.Filter.Regex(a => a.JobTitle, new BsonRegularExpression(jobTitleFilter, "i"));
            }
            return await _col.Find(filter).Skip(skip).Limit(limit).ToListAsync();
        }


        public async Task<Applicant> GetByIdAsync(ObjectId id)
        {
            return await _col.Find(a => a.Id == id).FirstOrDefaultAsync();
        }


        public async Task UpdateStatusAsync(ObjectId applicantId, string status)
        {
            var update = Builders<Applicant>.Update.Set(a => a.Status, status);
            await _col.UpdateOneAsync(a => a.Id == applicantId, update);
        }
    }
}
