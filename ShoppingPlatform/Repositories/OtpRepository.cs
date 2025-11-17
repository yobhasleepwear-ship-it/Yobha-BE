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

        // Create a new OTP entry
        public Task CreateAsync(OtpEntry e) => _col.InsertOneAsync(e);

        // Get latest OTP for a phone (no 'Used' filter since field removed)
        public Task<OtpEntry?> GetLatestForPhoneAsync(string phone) =>
            _col.Find(o => o.PhoneNumber == phone)
                .SortByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

        // Previously marked as used — now just update provider status instead
        public Task MarkUsedAsync(string id, string? providerStatus = null) =>
            _col.UpdateOneAsync(
                o => o.Id == id,
                Builders<OtpEntry>.Update
                    .Set(o => o.ProviderStatus, providerStatus ?? "USED")
                    .Set(o => o.ProviderRawResponse, "Marked as used")
                    .Set(o => o.Note, "OTP marked used at " + DateTime.UtcNow)
            );

        // Previously incremented attempts — now just store a note or log
        public Task IncrementAttemptsAsync(string id, string? note = null) =>
            _col.UpdateOneAsync(
                o => o.Id == id,
                Builders<OtpEntry>.Update
                    .Set(o => o.Note, note ?? "OTP reattempt at " + DateTime.UtcNow)
            );

        public async Task UpdateAsync(string id, OtpEntry entry)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(nameof(id));
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            var filter = Builders<OtpEntry>.Filter.Eq(x => x.Id, id);
            // Ensure the Id on the entry is correct
            entry.Id = id;

            var result = await _col.ReplaceOneAsync(filter, entry, new ReplaceOptions { IsUpsert = false });

            if (result.MatchedCount == 0)
            {
                throw new KeyNotFoundException($"OtpEntry with id '{id}' not found.");
            }
        }
        public async Task<OtpEntry?> GetBySessionIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException(nameof(sessionId));

            var filter = Builders<OtpEntry>.Filter.Eq(x => x.SessionId, sessionId);
            var sort = Builders<OtpEntry>.Sort.Descending(x => x.CreatedAt);

            return await _col.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync();
        }

    }
}
