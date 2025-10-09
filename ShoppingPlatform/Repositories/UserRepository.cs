using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShoppingPlatform.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

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

        // ----------------------
        // New: Find user by refresh token (any token)
        // ----------------------
        public Task<User?> GetByRefreshTokenAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return Task.FromResult<User?>(null);

            var filter = Builders<User>.Filter.ElemMatch(u => u.RefreshTokens, rt => rt.Token == token);
            return _collection.Find(filter).FirstOrDefaultAsync();
        }

        // ----------------------
        // New: Find user by refresh token and ensure the token is active (not revoked and not expired)
        // ----------------------
        public Task<User?> GetByRefreshTokenAsync(string token, bool onlyActive)
        {
            if (string.IsNullOrEmpty(token)) return Task.FromResult<User?>(null);

            if (!onlyActive)
                return GetByRefreshTokenAsync(token);

            // Build a filter that matches a refresh token with Token == token
            // AND (RevokedAt == null) AND (ExpiresAt > now)
            var now = DateTime.UtcNow;
            var elemFilter = Builders<RefreshToken>.Filter.And(
                Builders<RefreshToken>.Filter.Eq(rt => rt.Token, token),
                Builders<RefreshToken>.Filter.Eq(rt => rt.RevokedAt, null),
                Builders<RefreshToken>.Filter.Gt(rt => rt.ExpiresAt, now)
            );

            var filter = Builders<User>.Filter.ElemMatch(u => u.RefreshTokens, elemFilter);
            return _collection.Find(filter).FirstOrDefaultAsync();
        }
    }
}
