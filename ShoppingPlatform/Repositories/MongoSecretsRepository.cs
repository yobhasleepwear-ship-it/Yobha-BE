using MongoDB.Driver;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Repositories
{
    public class MongoSecretsRepository : ISecretsRepository
    {
        private readonly IMongoCollection<Secrets> _col;

        public MongoSecretsRepository(IMongoClient client, IConfiguration config)
        {
            var dbName = config.GetValue<string>("Mongo:Database") ?? "shoppingplatform";
            var collectionName = config.GetValue<string>("Mongo:SecretsCollection") ?? "secrets";
            var db = client.GetDatabase(dbName);
            _col = db.GetCollection<Secrets>(collectionName);
        }

        public async Task<Secrets?> GetSecretsByAddedForAsync(string addedFor)
        {
            var filter = Builders<Secrets>.Filter.Eq(s => s.AddedFor, addedFor);
            return await _col.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpsertSecretsAsync(Secrets secrets)
        {
            if (secrets == null) throw new ArgumentNullException(nameof(secrets));
            var filter = Builders<Secrets>.Filter.Eq(s => s.AddedFor, secrets.AddedFor);
            await _col.ReplaceOneAsync(filter, secrets, new ReplaceOptions { IsUpsert = true });
        }
    }
}
