using System.Text.RegularExpressions;

namespace ShoppingPlatform.Helpers
{
    public static class PhoneHelper
    {
        // Normalize phone numbers into digits only, and if no country code present,
        // you may choose to default to "91" (India). Adjust defaultCountryCode as needed.
        public static string Normalize(string? phone, string defaultCountryCode = "91")
        {
            if (string.IsNullOrWhiteSpace(phone))
                return string.Empty;

            // keep digits only
            var digits = Regex.Replace(phone, @"\D", string.Empty);

            // common patterns:
            // if starts with 0, drop leading zeros
            digits = digits.TrimStart('0');

            // if digits length looks like local 10-digit and does not start with country code, prefix default
            if (digits.Length == 10)
                digits = defaultCountryCode + digits;

            return digits;
        }
    }
}
