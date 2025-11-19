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
        private readonly ILoyaltyPointAuditService _loyaltyPointAuditService;
        public ReferralService(IReferralRepository refRepo, IMongoDatabase db,ILoyaltyPointAuditService loyaltyPointAuditService, int pointsToAward = 500)
        {
            _refRepo = refRepo;
            _db = db;
            _users = db.GetCollection<User>("users");
            _pointsToAward = pointsToAward;
            _loyaltyPointAuditService =  loyaltyPointAuditService;
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

            var normEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
            var normPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

            var referral = await _refRepo.FindUnredeemedByEmailOrPhoneAsync(normEmail ?? string.Empty, normPhone ?? string.Empty);
            if (referral == null) return (false, null); // no referral to redeem

            IClientSessionHandle? session = null;
            try
            {
                session = await _db.Client.StartSessionAsync();
                session.StartTransaction();

                // mark referral redeemed (ideally this repo method should accept session)
                var marked = await _refRepo.MarkRedeemedAsync(referral.Id!, newUserId);
                if (!marked)
                {
                    await session.AbortTransactionAsync();
                    return (false, "Referral could not be marked redeemed (possibly raced)");
                }

                // ---- SAFELY update loyalty points: read referrer, treat null as 0, set new value ----
                // NOTE: requires a session-aware GetByIdAsync on your UserRepository. If you don't have it,
                // either add it or use the pipeline approach from Option B.
                var referrer = await GetByIdAsync(session, referral.ReferrerUserId);
                if (referrer == null)
                {
                    await session.AbortTransactionAsync();
                    return (false, "Referrer user not found");
                }

                var currentPoints = referrer.LoyaltyPoints ?? 0;
                var newPoints = currentPoints + _pointsToAward;

                var update = Builders<User>.Update.Set(u => u.LoyaltyPoints, newPoints);
                var userRes = await _users.UpdateOneAsync(session, u => u.Id == referral.ReferrerUserId, update);
                if (userRes.ModifiedCount == 0)
                {
                    await session.AbortTransactionAsync();
                    return (false, "Failed to update referrer points");
                }
                var updateloyalty = _loyaltyPointAuditService.RecordSimpleAsync(referral.ReferrerUserId, "Credit", _pointsToAward, "Referral", null, email, phone, newPoints);
                await session.CommitTransactionAsync();
                return (true, null);
            }
            catch (Exception ex)
            {
                if (session != null)
                {
                    try { await session.AbortTransactionAsync(); } catch { }
                }

                // Fallback (best-effort) - non transactional
                try
                {
                    var markOk = await _refRepo.MarkRedeemedAsync(referral.Id!, newUserId);
                    if (!markOk) return (false, "Referral could not be marked redeemed (fallback)");

                    // fallback update using pipeline directly on collection if available
                    // (preferred fallback) OR read-modify-write without transaction:
                    var referrerFallback = await GetByIdAsync(referral.ReferrerUserId);
                    if (referrerFallback == null)
                    {
                        // attempt to undo referral mark if your repo supports undo; otherwise return failure
                        return (false, "Referrer user not found (fallback)");
                    }

                    var curr = referrerFallback.LoyaltyPoints ?? 0;
                    var newPts = curr + _pointsToAward;
                    var upd = Builders<User>.Update.Set(u => u.LoyaltyPoints, newPts);
                    var res = await _users.UpdateOneAsync(u => u.Id == referral.ReferrerUserId, upd);
                    if (res.ModifiedCount == 0)
                    {
                        // compensation left as TODO: implement UndoMarkRedeemed if you need strict consistency
                        return (false, "Referrer user not found (fallback update failed)");
                    }


                    var updateloyalty = _loyaltyPointAuditService.RecordSimpleAsync(referral.ReferrerUserId,"Credit", _pointsToAward, "Referral", null, email, phone,newPts);

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
        public async Task<User?> GetByIdAsync(IClientSessionHandle session, string userId)
        {
            return await _users
                .Find(session, u => u.Id == userId)
                .FirstOrDefaultAsync();
        }
        public async Task<User?> GetByIdAsync(string userId)
        {
            return await _users
                .Find(u => u.Id == userId)
                .FirstOrDefaultAsync();
        }


    }
}
