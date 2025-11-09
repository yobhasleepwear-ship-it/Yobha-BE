using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using ShoppingPlatform.Repositories;
using Microsoft.Extensions.Caching.Memory;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Helpers
{
    public class PaymentHelper
    {
        private readonly ILogger<PaymentHelper> _log;
        private readonly HttpClient _http;
        private readonly ISecretsRepository _secretsRepo;
        private readonly IMemoryCache _cache;

        public PaymentHelper(HttpClient httpClient, ILogger<PaymentHelper> log, ISecretsRepository secretsRepo, IMemoryCache cache)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _secretsRepo = secretsRepo ?? throw new ArgumentNullException(nameof(secretsRepo));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        // Creates a razorpay order. Use environment var keys for security.
        // 'isInternational' flag toggles which key pair to use.
        public async Task<string> CreateRazorpayOrderAsync(string orderId, decimal amount, string currency, bool isInternational = false)
        {
            // Razorpay expects amount in paise if INR (i.e. multiply by 100). For other currencies the API doc varies.
            // Use integer amount in smallest currency unit:
            long smallestUnit = ConvertToSmallestCurrencyUnit(amount, currency);

            var secrets = await GetRazorpaySecretsCachedAsync("RazorPay");
            if (secrets == null)
            {
                _log.LogError("Razorpay secrets not found for '{AddedFor}'", "RazorPay");
                throw new InvalidOperationException("Payment provider credentials are not configured.");
            }

            string? keyId = isInternational ? secrets.RAZOR_KEY_ID_INTL : secrets.RAZOR_KEY_ID_INR;
            string? keySecret = isInternational ? secrets.RAZOR_KEY_SECRET_INTL : secrets.RAZOR_KEY_SECRET_INR;

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                _log.LogError("Razorpay keys are not configured correctly for '{AddedFor}' (intl={IsInt})", "RazorPay", isInternational);
                throw new InvalidOperationException("Razorpay keys are not configured.");
            }

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));

            var payload = new
            {
                amount = smallestUnit,
                currency = currency,
                receipt = orderId,
                payment_capture = 1 // auto-capture; set 0 if you want manual capture
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var resp = await _http.SendAsync(request);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Razorpay order creation failed: {Status} {Body}", resp.StatusCode, body);
                throw new InvalidOperationException("Failed to create razorpay order");
            }

            using var doc = JsonDocument.Parse(body);
            var razorOrderId = doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Missing id in razor response");
            return razorOrderId;
        }
        private async Task<RazorPaySecrets?> GetRazorpaySecretsCachedAsync(string addedFor)
        {
            var cacheKey = $"secrets:razorpay:{addedFor}";

            if (_cache.TryGetValue(cacheKey, out RazorPaySecrets? cached) && cached != null)
                return cached;

            var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync(addedFor);
            if (secretsDoc == null || secretsDoc.razorPaySecrets == null)
                return null;

            return secretsDoc.razorPaySecrets;
        }


        private long ConvertToSmallestCurrencyUnit(decimal amount, string currency)
        {
            // Basic handling: INR -> paise (x100). For other currencies you may need special rules.
            // This helper can be extended per-currency.
            return (long)decimal.Round(amount * 100m);
        }
    }
}
