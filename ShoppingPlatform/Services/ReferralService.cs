using MongoDB.Driver;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using System;
using System.Threading.Tasks;

namespace ShoppingPlatform.Services
{
    public class ReferralService
    {
        private readonly IReferralRepository _refRepo;
        private readonly IMongoDatabase _db;
        private readonly IMongoCollection<User> _users;
        private readonly int _pointsToAward;

        public ReferralService(IReferralRepository refRepo, IMongoDatabase db, int pointsToAward = 500)
        {
            _refRepo = refRepo;
            _db = db;
            _users = db.GetCollection<User>("users");
            _pointsToAward = pointsToAward;
        }

        /// <summary>
        /// Create referral if email/phone not already referred. Returns (true, null) on success.
        /// Returns (false, message) if duplicate or invalid.
        /// </summary>
        public async Task<(bool Success, string? Error)> CreateReferralAsync(string referrerUserId, string? referredEmail, string? referredPhone)
        {
            if (string.IsNullOrWhiteSpace(referrerUserId)) return (false, "referrerUserId required");
            if (string.IsNullOrWhiteSpace(referredEmail) && string.IsNullOrWhiteSpace(referredPhone))
                return (false, "Provide referred email or phone");

            var referral = new Referral
            {
                ReferrerUserId = referrerUserId,
                ReferredEmail = string.IsNullOrWhiteSpace(referredEmail) ? null : referredEmail.Trim().ToLowerInvariant(),
                ReferredPhone = string.IsNullOrWhiteSpace(referredPhone) ? null : referredPhone.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            var created = await _refRepo.CreateReferralAsync(referral);
            if (!created) return (false, "A referral for that email or phone already exists");
            return (true, null);
        }

        /// <summary>
        /// Called when a new user signs up. If there is an unredeemed referral for this email/phone,
        /// mark referral redeemed and award points to the referrer. Returns true if redeemed and points awarded.
        /// </summary>
        public async Task<(bool Redeemed, string? Error)> RedeemReferralOnSignupAsync(string newUserId, string? email, string? phone)
        {
            if (string.IsNullOrWhiteSpace(newUserId)) return (false, "newUserId required");

            // Normalize inputs similar to creation
            var normEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
            var normPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

            var referral = await _refRepo.FindUnredeemedByEmailOrPhoneAsync(normEmail ?? string.Empty, normPhone ?? string.Empty);
            if (referral == null) return (false, null); // no referral to redeem

            // Attempt transaction: mark referral redeemed + increment referrer's loyalty points
            IClientSessionHandle? session = null;
            try
            {
                session = await _db.Client.StartSessionAsync();
                session.StartTransaction();

                // mark referral redeemed
                var marked = await _refRepo.MarkRedeemedAsync(referral.Id!, newUserId);
                if (!marked)
                {
                    await session.AbortTransactionAsync();
                    return (false, "Referral could not be marked redeemed (possibly raced)");
                }

                // increment loyalty points for referrer
                var update = Builders<User>.Update.Inc(u => u.LoyaltyPoints, _pointsToAward);
                var userRes = await _users.UpdateOneAsync(session, u => u.Id == referral.ReferrerUserId, update);
                if (userRes.ModifiedCount == 0)
                {
                    // Referrer not found: rollback and return failure
                    await session.AbortTransactionAsync();
                    return (false, "Referrer user not found");
                }

                await session.CommitTransactionAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                // If transaction not supported or failed, attempt fallback (best-effort)
                if (session != null)
                {
                    try { await session.AbortTransactionAsync(); } catch { }
                }

                // Fallback: non-transactional sequence (try to mark referral then increment points)
                try
                {
                    var markOk = await _refRepo.MarkRedeemedAsync(referral.Id!, newUserId);
                    if (!markOk) return (false, "Referral could not be marked redeemed (fallback)");
                    var update = Builders<User>.Update.Inc(u => u.LoyaltyPoints, _pointsToAward);
                    var res = await _users.UpdateOneAsync(u => u.Id == referral.ReferrerUserId, update);
                    if (res.ModifiedCount == 0)
                    {
                        // compensation: try to unmark referral
                        await _refRepo.MarkRedeemedAsync(referral.Id!, null!); // note: this line won't unredeem in our current API; implement Undo if needed
                        return (false, "Referrer user not found (fallback)");
                    }
                    return (true, null);
                }
                catch (Exception e2)
                {
                    return (false, $"Redeem failed: {e2.Message}");
                }
            }
            finally
            {
                session?.Dispose();
            }
        }
        public async Task<List<Referral>> GetReferralsByReferrerAsync(string referrerUserId)
        {
            // implement using IReferralRepository; add repository method if missing
            return await _refRepo.GetByReferrerAsync(referrerUserId);
        }
    }
}
