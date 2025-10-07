using MongoDB.Driver;
using ShoppingPlatform.Models;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class OtpRepository
    {
        private readonly IMongoCollection<OtpEntry> _col;

        public OtpRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<OtpEntry>("otps");
        }

        public Task CreateAsync(OtpEntry e) => _col.InsertOneAsync(e);

        public Task<OtpEntry?> GetLatestForPhoneAsync(string phone) =>
            _col.Find(o => o.PhoneNumber == phone && !o.Used)
                .SortByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

        public Task MarkUsedAsync(string id) =>
            _col.UpdateOneAsync(o => o.Id == id, Builders<OtpEntry>.Update.Set(o => o.Used, true));

        public Task IncrementAttemptsAsync(string id, int attempts) =>
            _col.UpdateOneAsync(o => o.Id == id, Builders<OtpEntry>.Update.Set(o => o.Attempts, attempts));


    }
}
