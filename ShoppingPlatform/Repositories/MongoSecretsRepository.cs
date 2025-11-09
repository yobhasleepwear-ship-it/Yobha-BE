using MongoDB.Driver;
using ShoppingPlatform.Models;
using System.Text.RegularExpressions;

namespace ShoppingPlatform.Repositories
{
    public class MongoSecretsRepository : ISecretsRepository
    {
        private readonly IMongoCollection<Secrets> _col;
        private readonly ILogger<MongoSecretsRepository> _log;

        public MongoSecretsRepository(IMongoDatabase db, ILogger<MongoSecretsRepository> log)
        {
            _col = db.GetCollection<Secrets>("secrets");
            _log = log;
        }

        public async Task<Secrets?> GetSecretsByAddedForAsync(string addedFor)
        {
            if (string.IsNullOrWhiteSpace(addedFor)) return null;
            // case-insensitive query:
            var filter = Builders<Secrets>.Filter.Regex(
                s => s.AddedFor,
                new MongoDB.Bson.BsonRegularExpression($"^{Regex.Escape(addedFor.Trim())}$", "i")
            );
            var found = await _col.Find(filter).FirstOrDefaultAsync();
            _log.LogDebug("SecretsRepository.GetSecretsByAddedForAsync({AddedFor}) -> found={Found}", addedFor, found != null);
            return found;
        }

        public async Task UpsertSecretsAsync(Secrets secrets)
        {
            var filter = Builders<Secrets>.Filter.Eq(s => s.AddedFor, secrets.AddedFor);
            await _col.ReplaceOneAsync(filter, secrets, new ReplaceOptions { IsUpsert = true });
        }
    }
}
