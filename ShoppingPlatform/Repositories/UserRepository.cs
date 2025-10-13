using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShoppingPlatform.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using MongoDB.Bson.Serialization.Attributes;

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

        // ----------------------
        // Address helpers (embedded addresses inside User document)
        // ----------------------

        /// <summary>
        /// Get addresses list for a user (returns empty list if none)
        /// </summary>
        public async Task<List<Address>> GetAddressesAsync(string userId)
        {
            var user = await GetByIdAsync(userId);
            return user?.Addresses ?? new List<Address>();
        }

        /// <summary>
        /// Add an address to user's Addresses list. If address.IsDefault == true,
        /// clears existing defaults first (so only one default remains).
        /// </summary>
        public async Task AddAddressAsync(string userId, Address address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));

            // ensure id exists
            if (string.IsNullOrEmpty(address.Id))
                address.Id = ObjectId.GenerateNewId().ToString();

            // If incoming address is default, unset existing defaults first
            if (address.IsDefault)
            {
                var unsetDefaults = Builders<User>.Update.Set("Addresses.$[].IsDefault", false);
                // Use a filter by user id to set all addresses' IsDefault = false
                await _collection.UpdateOneAsync(
                    Builders<User>.Filter.Eq(u => u.Id, userId),
                    unsetDefaults
                );
            }

            var push = Builders<User>.Update.Push(u => u.Addresses, address);
            await _collection.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, userId),
                push
            );
        }

        /// <summary>
        /// Update an embedded address (matched by address.Id). If updatedAddress.IsDefault == true,
        /// clears other defaults first. Returns true if an element was modified.
        /// </summary>
        public async Task<bool> UpdateAddressAsync(string userId, Address updatedAddress)
        {
            if (updatedAddress == null) throw new ArgumentNullException(nameof(updatedAddress));
            if (string.IsNullOrEmpty(updatedAddress.Id)) throw new ArgumentException("address.Id is required");

            // If marking as default, unset other defaults first (best-effort)
            if (updatedAddress.IsDefault)
            {
                var unsetDefaults = Builders<User>.Update.Set("Addresses.$[].IsDefault", false);
                await _collection.UpdateOneAsync(
                    Builders<User>.Filter.Eq(u => u.Id, userId),
                    unsetDefaults
                );
            }

            // Build a filter that matches the user AND the embedded address.
            // Try matching by ObjectId first (Addresses._id stored as ObjectId in many setups),
            // otherwise fall back to string match. This covers both representation styles.
            FilterDefinition<User> filter;

            if (MongoDB.Bson.ObjectId.TryParse(updatedAddress.Id, out var addressObjectId))
            {
                // match embedded _id as an ObjectId
                filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(u => u.Id, userId),
                    Builders<User>.Filter.Eq("Addresses._id", addressObjectId)
                );
            }
            else
            {
                // match embedded _id as raw string
                filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(u => u.Id, userId),
                    Builders<User>.Filter.Eq("Addresses._id", updatedAddress.Id)
                );
            }

            // Replace the matched embedded address document using the positional $ operator.
            var update = Builders<User>.Update.Set("Addresses.$", updatedAddress);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }



        /// <summary>
        /// Remove an address by id. Returns true if removed.
        /// </summary>
        public async Task<bool> RemoveAddressAsync(string userId, string addressId)
        {
            var update = Builders<User>.Update.PullFilter(u => u.Addresses, Builders<Address>.Filter.Eq(a => a.Id, addressId));
            var result = await _collection.UpdateOneAsync(Builders<User>.Filter.Eq(u => u.Id, userId), update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Update user's full name (lightweight partial update).
        /// </summary>
        public async Task<bool> UpdateUserNameAsync(string userId, string fullName)
        {
            var update = Builders<User>.Update.Set(u => u.FullName, fullName).Set(u => u.LastUpdatedAt, DateTime.UtcNow);
            var result = await _collection.UpdateOneAsync(Builders<User>.Filter.Eq(u => u.Id, userId), update);
            return result.ModifiedCount > 0;
        }
    }
}
