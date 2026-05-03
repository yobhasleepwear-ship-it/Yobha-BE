using ShoppingPlatform.Models;
using System.Text.RegularExpressions;

namespace ShoppingPlatform.Helpers
{
    public static class AddressNormalizationHelper
    {
        private static readonly Dictionary<string, string> CountryCodeByCountry = new(StringComparer.OrdinalIgnoreCase)
        {
            ["India"] = "91",
            ["United Arab Emirates"] = "971",
            ["Saudi Arabia"] = "966",
            ["Qatar"] = "974",
            ["Kuwait"] = "965",
            ["Oman"] = "968",
            ["Bahrain"] = "973",
            ["Jordan"] = "962",
            ["Lebanon"] = "961",
            ["Egypt"] = "20",
            ["Iraq"] = "964",
            ["Russia"] = "7",
            ["United Kingdom"] = "44",
            ["United States"] = "1",
            ["Pakistan"] = "92",
            ["Bangladesh"] = "880",
            ["Sri Lanka"] = "94",
            ["Nepal"] = "977",
            ["Philippines"] = "63",
            ["Indonesia"] = "62",
            ["Malaysia"] = "60",
            ["Singapore"] = "65",
            ["Thailand"] = "66",
            ["Vietnam"] = "84",
            ["Japan"] = "81",
            ["South Korea"] = "82",
            ["China"] = "86",
            ["Germany"] = "49",
            ["France"] = "33",
            ["Italy"] = "39",
            ["Spain"] = "34",
            ["Netherlands"] = "31",
            ["Australia"] = "61",
            ["New Zealand"] = "64",
            ["South Africa"] = "27",
            ["Nigeria"] = "234",
            ["Kenya"] = "254",
        };

        private static readonly Dictionary<string, string> CountryByCountryCode = CountryCodeByCountry
            .ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.OrdinalIgnoreCase);

        public static string NormalizeDigits(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value, @"\D", string.Empty);
        }

        public static string NormalizeCountryName(string? country, string? countryCode = null)
        {
            var trimmedCountry = (country ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(trimmedCountry) && trimmedCountry != "+91")
            {
                return trimmedCountry;
            }

            var normalizedCountryCode = NormalizeDigits(countryCode);
            return CountryByCountryCode.TryGetValue(normalizedCountryCode, out var resolvedCountry)
                ? resolvedCountry
                : trimmedCountry;
        }

        public static string InferCountryCode(string? country, string? countryCode)
        {
            var normalizedCountryCode = NormalizeDigits(countryCode);
            if (!string.IsNullOrWhiteSpace(normalizedCountryCode))
            {
                return normalizedCountryCode;
            }

            var normalizedCountry = NormalizeCountryName(country, countryCode);
            return CountryCodeByCountry.TryGetValue(normalizedCountry, out var resolvedCountryCode)
                ? resolvedCountryCode
                : string.Empty;
        }

        public static string ToStoredPhone(string? phone, string? countryCode)
        {
            var digits = NormalizeDigits(phone);
            var normalizedCountryCode = InferCountryCode(null, countryCode);

            if (string.IsNullOrWhiteSpace(digits) || string.IsNullOrWhiteSpace(normalizedCountryCode))
            {
                return digits;
            }

            return digits.StartsWith(normalizedCountryCode, StringComparison.Ordinal)
                ? digits
                : $"{normalizedCountryCode}{digits}";
        }

        public static Address NormalizeAddress(Address address)
        {
            var normalizedCountryCode = InferCountryCode(address.Country, address.countryCode);
            var normalizedCountry = NormalizeCountryName(address.Country, normalizedCountryCode);
            var normalizedPhone = ToStoredPhone(address.MobileNumner, normalizedCountryCode);

            address.Country = normalizedCountry;
            address.countryCode = normalizedCountryCode;
            address.MobileNumner = normalizedPhone;
            return address;
        }

        public static bool IsValidNormalizedAddress(Address? address, out string errorMessage)
        {
            if (address == null)
            {
                errorMessage = "Address is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(address.FullName)
                || string.IsNullOrWhiteSpace(address.Line1)
                || string.IsNullOrWhiteSpace(address.City)
                || string.IsNullOrWhiteSpace(address.State)
                || string.IsNullOrWhiteSpace(address.Zip)
                || string.IsNullOrWhiteSpace(address.Country))
            {
                errorMessage = "Address fields are required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(address.countryCode))
            {
                errorMessage = "countryCode is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(address.MobileNumner))
            {
                errorMessage = "Mobile number is required";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
