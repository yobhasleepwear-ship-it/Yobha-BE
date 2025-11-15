using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Models;
using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Helpers
{
    public class PaymentHelper
    {
        private readonly ILogger<PaymentHelper> _log;
        private readonly HttpClient _http;
        private readonly ISecretsRepository _secretsRepo;

        public PaymentHelper(HttpClient httpClient, ILogger<PaymentHelper> log, ISecretsRepository secretsRepo)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _secretsRepo = secretsRepo ?? throw new ArgumentNullException(nameof(secretsRepo));
        }

        // Creates a Razorpay order. Fetches keys directly from DB each time.
        public async Task<RazorpayOrderResult> CreateRazorpayOrderAsync(string orderId, decimal amount, string currency, bool isInternational = false)
        {
            var result = new RazorpayOrderResult
            {
                Success = false,
                StatusCode = 0,
                RequestPayload = string.Empty,
                ResponseBody = string.Empty,
                RazorpayOrderId = null,
                ErrorMessage = null
            };

            // convert to smallest unit (paise for INR)
            long smallestUnit = ConvertToSmallestCurrencyUnit(amount, currency);

            // Defensive check: Razorpay won't accept zero-amount orders
            if (smallestUnit <= 0)
            {
                result.ErrorMessage = "Amount must be greater than zero for Razorpay orders.";
                _log.LogWarning("Skipping Razorpay create for {OrderId} because smallestUnit={SmallestUnit}", orderId, smallestUnit);
                return result;
            }

            var payload = new
            {
                amount = smallestUnit,
                currency = currency,
                receipt = orderId,
                payment_capture = 1
            };

            string payloadString = JsonSerializer.Serialize(payload);
            result.RequestPayload = payloadString;

            // --- Fetch secrets directly from DB (no cache) ---
            var secrets = await GetRazorpaySecretsDirectAsync("RazorPay");
            if (secrets == null)
            {
                _log.LogError("Razorpay secrets not found for AddedFor='RazorPay'");
                result.ErrorMessage = "Payment provider credentials are not configured.";
                return result;
            }

            string? keyId = isInternational ? secrets.RAZOR_KEY_ID_INTL : secrets.RAZOR_KEY_ID_INR;
            string? keySecret = isInternational ? secrets.RAZOR_KEY_SECRET_INTL : secrets.RAZOR_KEY_SECRET_INR;

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                _log.LogError("Razorpay keys are not configured correctly (intl={IsInt}) for {OrderId}", isInternational, orderId);
                result.ErrorMessage = "Razorpay keys are not configured correctly in DB.";
                return result;
            }

            _log.LogInformation("Razorpay keys successfully loaded for {OrderId} (intl={IsInt})", orderId, isInternational);

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders")
            {
                Content = new StringContent(payloadString, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            try
            {
                _log.LogDebug("Sending Razorpay order create request for {OrderId} payloadLen={PayloadLen}", orderId, payloadString?.Length ?? 0);
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
            result.ResponseBody = body ?? string.Empty;

            // Log raw body (trimmed)
            _log.LogDebug("Razorpay response for {OrderId} status={Status} bodyLen={Len} preview={Preview}",
                orderId, resp.StatusCode, result.ResponseBody.Length,
                result.ResponseBody.Length > 300 ? result.ResponseBody[..300] + "..." : result.ResponseBody);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Razorpay order creation failed for {OrderId}: {Status} {Body}", orderId, resp.StatusCode, result.ResponseBody);
                result.ErrorMessage = $"Razorpay error: {(int)resp.StatusCode} - {Truncate(result.ResponseBody, 800)}";
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
                    _log.LogInformation("Razorpay order created for {OrderId} razorId={RazorId}", orderId, result.RazorpayOrderId);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Razorpay response missing 'id'.";
                    _log.LogWarning("Razorpay response missing 'id' for {OrderId}", orderId);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to parse Razorpay response JSON for {OrderId}", orderId);
                result.ErrorMessage = "Invalid JSON from Razorpay.";
                result.Success = false;
            }

            return result;
        }

        public async Task<RefundResult> CreateRefundAsync(string paymentId, decimal amountInRupees, bool useInstant = true, Dictionary<string, string>? notes = null)
        {
            if (string.IsNullOrWhiteSpace(paymentId))
                throw new ArgumentNullException(nameof(paymentId));

            var result = new RefundResult { Success = false, StatusCode = 0 };

            // convert to smallest unit (paise for INR) using your helper
            long paise = ConvertToSmallestCurrencyUnit(amountInRupees, "INR");
            if (paise <= 0)
            {
                result.ErrorMessage = "Refund amount must be greater than zero.";
                return result;
            }

            // Fetch Razorpay secrets
            var secrets = await GetRazorpaySecretsDirectAsync("RazorPay");
            if (secrets == null)
            {
                result.ErrorMessage = "Payment provider credentials are not configured.";
                _log.LogError("CreateRefundAsync: Razorpay secrets not found.");
                return result;
            }

            string? keyId = secrets.RAZOR_KEY_ID_INR;
            string? keySecret = secrets.RAZOR_KEY_SECRET_INR;
            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            {
                result.ErrorMessage = "Razorpay keys are not configured correctly in DB.";
                _log.LogError("CreateRefundAsync: Razorpay keys missing.");
                return result;
            }

            var payload = new Dictionary<string, object>()
            {
                ["amount"] = paise,
                ["speed"] = useInstant ? "optimum" : "normal"
            };

            if (notes != null && notes.Any())
            {
                payload["notes"] = notes;
            }

            string payloadString = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.razorpay.com/v1/payments/{paymentId}/refund")
            {
                Content = new StringContent(payloadString, Encoding.UTF8, "application/json")
            };

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage resp;
            try
            {
                _log.LogDebug("Sending Razorpay refund request for paymentId={PaymentId} payload={PayloadPreview}", paymentId, Truncate(payloadString, 200));
                resp = await _http.SendAsync(request);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "HTTP request to Razorpay refund failed for payment {PaymentId}", paymentId);
                result.ErrorMessage = ex.Message;
                return result;
            }

            result.StatusCode = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync();
            result.RawResponse = body ?? string.Empty;

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Razorpay refund failed for payment {PaymentId}: {Status} {Body}", paymentId, resp.StatusCode, body);
                result.ErrorMessage = $"Razorpay error: {(int)resp.StatusCode} - {Truncate(body, 1000)}";
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp))
                    result.RefundId = idProp.GetString();

                if (root.TryGetProperty("payment_id", out var payProp))
                    result.PaymentId = payProp.GetString();

                if (root.TryGetProperty("status", out var st))
                    result.Status = st.GetString();

                if (root.TryGetProperty("amount", out var amt))
                {
                    // amount returned is in paise -> convert to rupees
                    long paiseResp = amt.GetInt64();
                    result.Amount = paiseResp / 100m;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to parse Razorpay refund JSON for payment {PaymentId}", paymentId);
                result.ErrorMessage = "Invalid JSON from Razorpay refund response.";
            }

            return result;
        }

        // --- New direct secret fetch without caching ---
        private async Task<RazorPaySecrets?> GetRazorpaySecretsDirectAsync(string addedFor)
        {
            try
            {
                var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync(addedFor);
                if (secretsDoc == null || secretsDoc.razorPaySecrets == null)
                {
                    _log.LogWarning("No Razorpay secrets document found in DB for AddedFor={AddedFor}", addedFor);
                    return null;
                }

                var s = secretsDoc.razorPaySecrets;
                _log.LogInformation("Razorpay secrets fetched from DB: hasINR={HasINR} hasINRSecret={HasINRSecret} hasINTL={HasINTL} hasINTLSecret={HasINTLSecret}",
                    !string.IsNullOrWhiteSpace(s.RAZOR_KEY_ID_INR),
                    !string.IsNullOrWhiteSpace(s.RAZOR_KEY_SECRET_INR),
                    !string.IsNullOrWhiteSpace(s.RAZOR_KEY_ID_INTL),
                    !string.IsNullOrWhiteSpace(s.RAZOR_KEY_SECRET_INTL)
                );

                return s;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error fetching Razorpay secrets from DB for {AddedFor}", addedFor);
                return null;
            }
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        private long ConvertToSmallestCurrencyUnit(decimal amount, string currency)
        {
            // INR -> paise (x100)
            return (long)decimal.Round(amount * 100m);
        }
    }
}
