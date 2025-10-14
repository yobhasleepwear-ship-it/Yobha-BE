using MongoDB.Driver;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Services
{
    public interface INewsletterService
    {
        Task<NewsletterEntry> AddAsync(NewsletterCreateDto dto, CancellationToken ct = default);
    }

    public class NewsletterService : INewsletterService
    {
        private readonly IMongoCollection<NewsletterEntry> _col;
        public NewsletterService(IMongoDatabase db) => _col = db.GetCollection<NewsletterEntry>("newsletter");

        public async Task<NewsletterEntry> AddAsync(NewsletterCreateDto dto, CancellationToken ct = default)
        {
            var entry = new NewsletterEntry
            {
                Email = dto.Email.Trim().ToLowerInvariant(),
                CountryCode = dto.CountryCode,
                PhoneNumber = dto.PhoneNumber,
                CreatedAt = DateTime.UtcNow
            };

            // optional: enforce unique email to avoid duplicates
            // await _col.UpdateOneAsync(x => x.Email == entry.Email,
            //     Builders<NewsletterEntry>.Update
            //       .SetOnInsert(x => x.Email, entry.Email)
            //       .SetOnInsert(x => x.CreatedAt, entry.CreatedAt)
            //       .Set(x => x.CountryCode, entry.CountryCode)
            //       .Set(x => x.PhoneNumber, entry.PhoneNumber),
            //     new UpdateOptions { IsUpsert = true }, ct);

            await _col.InsertOneAsync(entry, cancellationToken: ct);
            return entry;
        }
    }

}
