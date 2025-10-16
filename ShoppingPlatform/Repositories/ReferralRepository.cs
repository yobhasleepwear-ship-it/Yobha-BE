using MongoDB.Driver;
using ShoppingPlatform.Models;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public class ReferralRepository : IReferralRepository
    {
        private readonly IMongoCollection<Referral> _referrals;

        public ReferralRepository(IMongoDatabase db)
        {
            _referrals = db.GetCollection<Referral>("referrals");
            // Ensure indexes at construction (best-effort)
            EnsureIndexesAsync().GetAwaiter().GetResult();
        }

        public async Task EnsureIndexesAsync()
        {
            // Unique sparse index on ReferredEmail
            var emailIndex = Builders<Referral>.IndexKeys.Ascending(r => r.ReferredEmail);
            var emailOptions = new CreateIndexOptions { Unique = true, Sparse = true };
            await _referrals.Indexes.CreateOneAsync(new CreateIndexModel<Referral>(emailIndex, emailOptions));

            // Unique sparse index on ReferredPhone
            var phoneIndex = Builders<Referral>.IndexKeys.Ascending(r => r.ReferredPhone);
            var phoneOptions = new CreateIndexOptions { Unique = true, Sparse = true };
            await _referrals.Indexes.CreateOneAsync(new CreateIndexModel<Referral>(phoneIndex, phoneOptions));
        }

        /// <summary>
        /// Create a referral. Returns false if a referral for same email/phone already exists (uniqueness).
        /// </summary>
        public async Task<bool> CreateReferralAsync(Referral referral)
        {
            if (referral == null) throw new ArgumentNullException(nameof(referral));
            if (string.IsNullOrWhiteSpace(referral.ReferrerUserId)) throw new ArgumentException("ReferrerUserId is required");

            // Normalize phone/email if you want - keep simple here
            try
            {
                await _referrals.InsertOneAsync(referral);
                return true;
            }
            catch (MongoWriteException mwx) when (mwx.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // duplicate referred email or phone
                return false;
            }
        }

        /// <summary>
        /// Finds an unredeemed referral matching email or phone (either param can be null/empty).
        /// Returns first matching unredeemed referral.
        /// </summary>
        public async Task<Referral?> FindUnredeemedByEmailOrPhoneAsync(string email, string phone)
        {
            var builder = Builders<Referral>.Filter;
            var filters = builder.Eq(r => r.IsRedeemed, false);

            var orParts = builder.Empty;
            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPhone = !string.IsNullOrWhiteSpace(phone);

            if (!hasEmail && !hasPhone) return null;

            if (hasEmail && hasPhone)
            {
                orParts = builder.Or(
                    builder.Eq(r => r.ReferredEmail, email.Trim().ToLowerInvariant()),
                    builder.Eq(r => r.ReferredPhone, phone.Trim())
                );
            }
            else if (hasEmail)
            {
                orParts = builder.Eq(r => r.ReferredEmail, email.Trim().ToLowerInvariant());
            }
            else // only phone
            {
                orParts = builder.Eq(r => r.ReferredPhone, phone.Trim());
            }

            var final = builder.And(filters, orParts);
            return await _referrals.Find(final).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Marks referral as redeemed (sets IsRedeemed=true, ReferredUserId and RedeemedAt).
        /// Returns true if matched & modified.
        /// </summary>
        public async Task<bool> MarkRedeemedAsync(string referralId, string referredUserId)
        {
            var update = Builders<Referral>.Update
                .Set(r => r.IsRedeemed, true)
                .Set(r => r.ReferredUserId, referredUserId)
                .Set(r => r.RedeemedAt, DateTime.UtcNow);

            var res = await _referrals.UpdateOneAsync(r => r.Id == referralId && r.IsRedeemed == false, update);
            return res.ModifiedCount > 0;
        }

        public async Task<List<Referral>> GetByReferrerAsync(string referrerUserId)
        {
            var filter = Builders<Referral>.Filter.Eq(r => r.ReferrerUserId, referrerUserId);
            return await _referrals.Find(filter)
                .SortByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

    }
}
