using ShoppingPlatform.Models;
using System.Threading.Tasks;

namespace ShoppingPlatform.Repositories
{
    public interface IReferralRepository
    {
        Task<bool> CreateReferralAsync(Referral referral); // returns false if duplicate exists
        Task<Referral?> FindUnredeemedByEmailOrPhoneAsync(string email, string phone);
        Task<bool> MarkRedeemedAsync(string referralId, string referredUserId);
        Task EnsureIndexesAsync();
    }
}
