using MongoDB.Driver;
using ShoppingPlatform.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ShoppingPlatform.Repositories
{
    public class UserRepository
    {
        private readonly IMongoCollection<User> _collection;

        public UserRepository(IMongoDatabase db)
        {
            _collection = db.GetCollection<User>("users");
        }

        public Task<List<User>> GetAllAsync() =>
            _collection.Find(_ => true).ToListAsync();

        public Task<User?> GetByIdAsync(string id) =>
            _collection.Find(u => u.Id == id).FirstOrDefaultAsync();

        public Task<User?> GetByEmailAsync(string email) =>
            _collection.Find(u => u.Email.ToLower() == email.ToLower()).FirstOrDefaultAsync();

        public Task CreateAsync(User user) =>
            _collection.InsertOneAsync(user);

        public Task UpdateAsync(string id, User user) =>
            _collection.ReplaceOneAsync(u => u.Id == id, user);

        public Task DeleteAsync(string id) =>
            _collection.DeleteOneAsync(u => u.Id == id);

        public Task<User?> GetByPhoneAsync(string phone) =>
            _collection.Find(u => u.PhoneNumber == phone).FirstOrDefaultAsync();

    }
}
