using System;
using System.Security.Cryptography;
using System.Text;

namespace ShoppingPlatform.Helpers
{
    public static class OtpHelper
    {
        public static string GenerateNumericOtp(int length = 6)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(RandomNumberGenerator.GetInt32(0, 10));
            return sb.ToString();
        }

        public static (string hashed, string salt) HashOtp(string otp)
        {
            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var salt = Convert.ToBase64String(saltBytes);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(otp));
            return (Convert.ToBase64String(hashBytes), salt);
        }

        public static bool VerifyOtp(string otp, string salt, string hashed)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(otp));
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(hashed), hash);
        }
    }
}
