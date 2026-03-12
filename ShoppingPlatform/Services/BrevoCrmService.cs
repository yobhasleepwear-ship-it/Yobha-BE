using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.Services
{
    public interface IBrevoCrmService
    {
        Task<bool> TrackSignupAsync(User user, CancellationToken ct = default);
        Task<bool> TrackOrderPlacedAsync(Order order, User? user = null, CancellationToken ct = default);
        Task<bool> TrackCartAbandonedAsync(User user, IEnumerable<CartItem> cartItems, DateTime cartUpdatedAtUtc, CancellationToken ct = default);
    }

    public class BrevoCrmService : IBrevoCrmService
    {
        private readonly HttpClient _http;
        private readonly BrevoSettings _settings;
        private readonly ILogger<BrevoCrmService> _logger;

        public BrevoCrmService(HttpClient http, IOptions<BrevoSettings> options, ILogger<BrevoCrmService> logger)
        {
            _http = http;
            _settings = options.Value;
            _logger = logger;
        }

        public Task<bool> TrackSignupAsync(User user, CancellationToken ct = default)
        {
            var email = user.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogInformation("Brevo signup skipped: missing email for user {UserId}", user.Id);
                return Task.FromResult(false);
            }

            var attrs = new Dictionary<string, object?>
            {
                ["FULLNAME"] = user.FullName,
                ["PHONE"] = user.PhoneNumber,
                ["SIGNUP_AT"] = DateTime.UtcNow.ToString("O"),
                ["LAST_EVENT"] = "signup"
            };

            return UpsertContactAsync(email, attrs, BuildListIds(_settings.SignupListId), ct);
        }

        public Task<bool> TrackOrderPlacedAsync(Order order, User? user = null, CancellationToken ct = default)
        {
            var email = ResolveOrderEmail(order, user);
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogInformation("Brevo order tracking skipped: missing email for order {OrderNumber}", order.OrderNumber);
                return Task.FromResult(false);
            }

            var attrs = new Dictionary<string, object?>
            {
                ["FULLNAME"] = user?.FullName ?? order.ShippingAddress?.FullName,
                ["PHONE"] = user?.PhoneNumber ?? order.ShippingAddress?.MobileNumner,
                ["LAST_EVENT"] = "order_placed",
                ["LAST_ORDER_ID"] = order.OrderNumber,
                ["LAST_ORDER_TOTAL"] = order.Total,
                ["LAST_ORDER_CURRENCY"] = order.Currency,
                ["LAST_ORDER_AT"] = DateTime.UtcNow.ToString("O"),
                ["LAST_ORDER_PAYMENT_METHOD"] = order.PaymentMethod,
                ["LAST_ORDER_PAYMENT_STATUS"] = order.PaymentStatus
            };

            return UpsertContactAsync(email!, attrs, BuildListIds(_settings.OrderPlacedListId), ct);
        }

        public Task<bool> TrackCartAbandonedAsync(User user, IEnumerable<CartItem> cartItems, DateTime cartUpdatedAtUtc, CancellationToken ct = default)
        {
            var email = user.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogInformation("Brevo cart-abandon skipped: missing email for user {UserId}", user.Id);
                return Task.FromResult(false);
            }

            var items = cartItems?.ToList() ?? new List<CartItem>();
            var itemCount = items.Sum(i => i.Quantity);
            var total = items.Sum(i => i.Price * i.Quantity);

            var attrs = new Dictionary<string, object?>
            {
                ["FULLNAME"] = user.FullName,
                ["PHONE"] = user.PhoneNumber,
                ["LAST_EVENT"] = "cart_abandoned",
                ["ABANDONED_CART_AT"] = cartUpdatedAtUtc.ToString("O"),
                ["ABANDONED_CART_ITEM_COUNT"] = itemCount,
                ["ABANDONED_CART_VALUE"] = total,
                ["ABANDONED_CART_CURRENCY"] = items.FirstOrDefault()?.Currency ?? "INR"
            };

            return UpsertContactAsync(email, attrs, BuildListIds(_settings.CartAbandonedListId), ct);
        }

        private async Task<bool> UpsertContactAsync(string email, Dictionary<string, object?> attributes, List<int>? listIds, CancellationToken ct)
        {
            if (!_settings.Enabled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _logger.LogWarning("Brevo is enabled but ApiKey is empty. Skipping CRM event for {Email}", email);
                return false;
            }

            try
            {
                var payload = new Dictionary<string, object?>
                {
                    ["email"] = email,
                    ["updateEnabled"] = true,
                    ["attributes"] = attributes
                };

                if (listIds is { Count: > 0 })
                {
                    payload["listIds"] = listIds;
                }

                var req = new HttpRequestMessage(HttpMethod.Post, "contacts")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };

                req.Headers.Add("api-key", _settings.ApiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Brevo upsert failed. Status={Status} Email={Email} Body={Body}", (int)resp.StatusCode, email, body);
                    return false;
                }
                _logger.LogInformation("Brevo upsert succeeded for {Email}", email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brevo upsert exception for {Email}", email);
                return false;
            }
        }

        private List<int>? BuildListIds(params int?[] ids)
        {
            var valid = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            return valid.Count == 0 ? null : valid;
        }

        private static string? ResolveOrderEmail(Order order, User? user)
        {
            return user?.Email?.Trim().ToLowerInvariant()
                ?? order.Email?.Trim().ToLowerInvariant();
        }
    }
}
