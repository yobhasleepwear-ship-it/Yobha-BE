using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using ShoppingPlatform.Repositories;
using Microsoft.Extensions.Caching.Memory;
using ShoppingPlatform.Models;
using ShoppingPlatform.DTOs;

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
        public async Task<RazorpayOrderResult> CreateRazorpayOrderAsync(string orderId, decimal amount, string currency, bool isInternational = false)
        {
            var result = new RazorpayOrderResult
            {
                Success = false, // default to false — set true on success
                StatusCode = 0,
                RequestPayload = string.Empty,
                ResponseBody = string.Empty
            };

            // convert to smallest unit (paise for INR)
            long smallestUnit = ConvertToSmallestCurrencyUnit(amount, currency);
            // Build payload
            var payload = new
            {
                amount = smallestUnit,
                currency = currency,
                receipt = orderId,
                payment_capture = 1
            };

            string payloadString = JsonSerializer.Serialize(payload);
            result.RequestPayload = payloadString;

            var secrets = await GetRazorpaySecretsCachedAsync("RazorPay");
            if (secrets == null)
            {
                _log.LogError("Razorpay secrets not found for '{AddedFor}'", "RazorPay");
                result.ErrorMessage = "Payment provider credentials are not configured.";
                return result;
            }

            string? keyId = isInternational ? secrets.RAZOR_KEY_ID_INTL : secrets.RAZOR_KEY_ID_INR;
            string? keySecret = isInternational ? secrets.RAZOR_KEY_SECRET_INTL : secrets.RAZOR_KEY_SECRET_INR;

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                _log.LogError("Razorpay keys are not configured correctly for '{AddedFor}' (intl={IsInt})", "RazorPay", isInternational);
                result.ErrorMessage = "Razorpay keys are not configured.";
                return result;
            }

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders")
            {
                Content = new StringContent(payloadString, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(request);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "HTTP request to Razorpay failed for order {OrderId}", orderId);
                result.ErrorMessage = $"HTTP error: {ex.Message}";
                return result;
            }

            var body = await resp.Content.ReadAsStringAsync();
            result.StatusCode = (int)resp.StatusCode;
            result.ResponseBody = body;

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Razorpay order creation failed: {Status} {Body}", resp.StatusCode, body);
                result.ErrorMessage = $"Razorpay error: {(int)resp.StatusCode}";
                result.Success = false;
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idProp))
                {
                    result.RazorpayOrderId = idProp.GetString();
                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Razorpay response missing 'id'.";
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to parse Razorpay response JSON for order {OrderId}", orderId);
                result.ErrorMessage = "Invalid JSON from Razorpay";
                result.Success = false;
            }

            return result;
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
