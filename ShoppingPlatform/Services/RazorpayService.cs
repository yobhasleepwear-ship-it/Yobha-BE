using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace ShoppingPlatform.Services
{
    public class RazorpayOrderResponse
    {
        // Razorpay response fields (we only map the ones we need)
        [JsonProperty("id")]
        public string id { get; set; } = null!;

        [JsonProperty("amount")]
        public long amount { get; set; }

        [JsonProperty("currency")]
        public string currency { get; set; } = null!;

        [JsonProperty("status")]
        public string status { get; set; } = null!;

        [JsonProperty("receipt")]
        public string receipt { get; set; } = null!;
    }

    public interface IRazorpayService
    {
        /// <summary>
        /// Creates a razorpay order. amountDecimal is INR amount (e.g. 123.45).
        /// Returns the raw Razorpay order response (id is the razorpay order id).
        /// </summary>
        Task<RazorpayOrderResponse> CreateOrderAsync(decimal amountDecimal, string currency, string receipt);

        /// <summary>
        /// Verify client-side payment signature sent after checkout: signature = HMAC_SHA256(orderId + "|" + paymentId, keySecret)
        /// </summary>
        bool VerifyPaymentSignature(string orderId, string paymentId, string signature);

        /// <summary>
        /// Verify webhook signature. Razorpay can send signature in hex or base64; this method will try both.
        /// </summary>
        bool VerifyWebhookSignature(string body, string signatureHeader, string webhookSecret);

        /// <summary>
        /// Razorpay KeyId (public) — used on client to start checkout.
        /// </summary>
        string KeyId { get; }
    }

    public class RazorpayService : IRazorpayService, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _keyId;
        private readonly string _keySecret;
        private readonly string _webhookSecret;
        private bool _disposed;

        public string KeyId => _keyId;

        public RazorpayService(IConfiguration config, HttpClient httpClient)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _keyId = config["Razorpay:KeyId"] ?? throw new ArgumentNullException("Razorpay:KeyId");
            _keySecret = config["Razorpay:KeySecret"] ?? throw new ArgumentNullException("Razorpay:KeySecret");
            _webhookSecret = config["Razorpay:WebhookSecret"] ?? string.Empty;

            var byteArray = Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            _http.BaseAddress = new Uri("https://api.razorpay.com/v1/");
        }

        /// <summary>
        /// Creates a Razorpay order. Amount passed as decimal INR (e.g. 123.45).
        /// Razorpay expects amount in paise (integer).
        /// </summary>
        public async Task<RazorpayOrderResponse> CreateOrderAsync(decimal amountDecimal, string currency, string receipt)
        {
            // Razorpay expects amount in smallest currency unit (paise)
            var amountInPaise = Convert.ToInt64(Math.Round(amountDecimal * 100M, 0, MidpointRounding.AwayFromZero));

            var payload = new
            {
                amount = amountInPaise,
                currency = currency,
                receipt = receipt,
                payment_capture = 1 // auto-capture; change to 0 if you want manual capture
            };

            var json = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await _http.PostAsync("orders", content).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                // include response body to make errors easier to debug in logs
                throw new InvalidOperationException($"Razorpay CreateOrder failed: {(int)resp.StatusCode} {resp.ReasonPhrase} - {body}");
            }

            var orderResp = JsonConvert.DeserializeObject<RazorpayOrderResponse>(body);
            if (orderResp == null || string.IsNullOrWhiteSpace(orderResp.id))
                throw new InvalidOperationException("Razorpay returned invalid order response.");

            return orderResp;
        }

        /// <summary>
        /// Verify signature returned to client after checkout. Uses HMAC-SHA256(orderId + "|" + paymentId, keySecret).
        /// Uses constant-time equality for comparison.
        /// </summary>
        public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
        {
            if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(signature))
                return false;

            var payload = $"{orderId}|{paymentId}";
            var expectedHex = ComputeHmacSha256Hex(payload, _keySecret);
            return SecureEqualsHex(expectedHex, signature);
        }

        /// <summary>
        /// Verify webhook signature using webhook secret. Accepts signatureHeader in hex or base64 format.
        /// Razorpay docs describe HMAC-SHA256(body, webhook_secret).
        /// </summary>
        public bool VerifyWebhookSignature(string body, string signatureHeader, string webhookSecret)
        {
            if (string.IsNullOrEmpty(webhookSecret) || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(body))
                return false;

            // compute HMAC-SHA256 hex
            var expectedHex = ComputeHmacSha256Hex(body, webhookSecret);
            if (SecureEqualsHex(expectedHex, signatureHeader))
                return true;

            // compute HMAC-SHA256 as base64 and compare (cover both formats)
            var expectedBase64 = ComputeHmacSha256Base64(body, webhookSecret);
            return SecureEquals(expectedBase64, signatureHeader);
        }

        private static string ComputeHmacSha256Hex(string text, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var textBytes = Encoding.UTF8.GetBytes(text);
            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(textBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private static string ComputeHmacSha256Base64(string text, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var textBytes = Encoding.UTF8.GetBytes(text);
            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(textBytes);
            return Convert.ToBase64String(hashBytes);
        }

        // constant-time compare for hex strings (normalize to lowercase)
        private static bool SecureEqualsHex(string aHexLower, string b)
        {
            if (string.IsNullOrEmpty(aHexLower) || string.IsNullOrEmpty(b)) return false;

            // normalize both to lowercase hex (if b is base64 this will fail which is fine)
            var bLower = b.Replace("-", "").ToLowerInvariant();

            // If b contains non-hex chars or length mismatch -> fail quickly
            if (bLower.Length != aHexLower.Length) return false;

            var aBytes = HexStringToBytes(aHexLower);
            var bBytes = HexStringToBytes(bLower);
            if (aBytes == null || bBytes == null) return false;

//#if NET6_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
//#else
//            return FixedTimeEqualsFallback(aBytes, bBytes);
//#endif
        }

        // constant-time compare for base64 or general string equality
        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);

//#if NET6_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
//#else
//            return FixedTimeEqualsFallback(aBytes, bBytes);
//#endif
        }

        private static byte[]? HexStringToBytes(string hex)
        {
            try
            {
                int len = hex.Length;
                var bytes = new byte[len / 2];
                for (int i = 0; i < len; i += 2)
                    bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        // fallback constant-time comparer for older frameworks
        private static bool FixedTimeEqualsFallback(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _http?.Dispose();
            _disposed = true;
        }
    }
}
