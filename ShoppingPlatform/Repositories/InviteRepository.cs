namespace ShoppingPlatform.Repositories
{
    using MongoDB.Driver;
    using ShoppingPlatform.Models;
    using System.ComponentModel;

    public class InviteRepository
    {
        private readonly IMongoCollection<Invite> _collection;

        public InviteRepository(IMongoDatabase db)
        {
            _collection = db.GetCollection<Invite>("invites");
        }

        public Task CreateAsync(Invite invite) => _collection.InsertOneAsync(invite);

        public Task<Invite?> GetByTokenAsync(string token) =>
            _collection.Find(i => i.Token == token && !i.Used && i.ExpiresAt > DateTime.UtcNow).FirstOrDefaultAsync();

        public Task MarkUsedAsync(string id) =>
            _collection.UpdateOneAsync(i => i.Id == id, Builders<Invite>.Update.Set(i => i.Used, true));
    }
}
