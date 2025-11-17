using System.Security.Cryptography;
using System.Text;

namespace ShoppingPlatform.Helpers
{
    public static class OtpHashHelper
    {
        // Replace this with configuration or user secret.
        private const string OTP_HASH_SECRET = "d3F!9Kp$72Lm@xQ#qrT6VpZ0W!sdF$JA";

        public static string HashOtp(string otp)
        {
            var key = Encoding.UTF8.GetBytes(OTP_HASH_SECRET);
            using var hmac = new HMACSHA256(key);
            var bytes = Encoding.UTF8.GetBytes(otp);
            var hash = hmac.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        public static bool Verify(string otp, string storedHash)
        {
            var computed = HashOtp(otp);
            return CryptographicEquals(Convert.FromBase64String(computed), Convert.FromBase64String(storedHash));
        }

        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

}
