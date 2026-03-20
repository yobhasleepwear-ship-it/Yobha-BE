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

        public async Task<bool> TrackOrderPlacedAsync(Order order, User? user = null, CancellationToken ct = default)
        {
            var email = ResolveOrderEmail(order, user);
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogInformation("Brevo order tracking skipped: missing email for order {OrderNumber}", order.OrderNumber);
                return false;
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

            var contactOk = await UpsertContactAsync(email!, attrs, BuildListIds(_settings.OrderPlacedListId), ct);

            var eventProps = new Dictionary<string, object?>
            {
                ["order_id"] = order.OrderNumber,
                ["payment_method"] = order.PaymentMethod,
                ["payment_status"] = order.PaymentStatus,
                ["status"] = order.Status,
                ["currency"] = order.Currency,
                ["subtotal"] = order.SubTotal,
                ["shipping"] = order.Shipping,
                ["tax"] = order.Tax,
                ["discount"] = order.Discount,
                ["total"] = order.Total,
                ["coupon_code"] = order.CouponCode,
                ["gift_card_number"] = order.GiftCardNumber,
                ["country"] = order.orderCountry,
                ["is_gift_wrap"] = order.isGiftWrap,
                ["items"] = BuildOrderItems(order.Items)
            };

            var eventOk = await TrackEventAsync(
                eventName: "order_placed",
                email: email!,
                eventDateUtc: order.UpdatedAt ?? order.CreatedAt,
                eventProperties: eventProps,
                contactProperties: BuildEventContactProperties(user?.FullName ?? order.ShippingAddress?.FullName, user?.PhoneNumber ?? order.ShippingAddress?.MobileNumner),
                ct: ct);

            _logger.LogInformation(
                "Brevo order_placed sync finished for {Email}. ContactOk={ContactOk} EventOk={EventOk} ListId={ListId} Order={OrderNumber}",
                email,
                contactOk,
                eventOk,
                _settings.OrderPlacedListId,
                order.OrderNumber);

            return contactOk || eventOk;
        }

        public async Task<bool> TrackCartAbandonedAsync(User user, IEnumerable<CartItem> cartItems, DateTime cartUpdatedAtUtc, CancellationToken ct = default)
        {
            var email = user.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogInformation("Brevo cart-abandon skipped: missing email for user {UserId}", user.Id);
                return false;
            }

            var items = cartItems?.ToList() ?? new List<CartItem>();
            var itemCount = items.Sum(i => i.Quantity);
            var total = items.Sum(i => i.Price * i.Quantity);
            var currency = items.FirstOrDefault()?.Currency ?? "INR";

            var attrs = new Dictionary<string, object?>
            {
                ["FULLNAME"] = user.FullName,
                ["PHONE"] = user.PhoneNumber,
                ["LAST_EVENT"] = "cart_abandoned",
                ["ABANDONED_CART_AT"] = cartUpdatedAtUtc.ToString("O"),
                ["ABANDONED_CART_ITEM_COUNT"] = itemCount,
                ["ABANDONED_CART_VALUE"] = total,
                ["ABANDONED_CART_CURRENCY"] = currency
            };

            var contactOk = await UpsertContactAsync(email, attrs, BuildListIds(_settings.CartAbandonedListId), ct);

            var eventProps = new Dictionary<string, object?>
            {
                ["cart_updated_at"] = cartUpdatedAtUtc.ToString("O"),
                ["item_count"] = itemCount,
                ["value"] = total,
                ["currency"] = currency,
                ["items"] = BuildCartItems(items)
            };

            var eventOk = await TrackEventAsync(
                eventName: "cart_abandoned",
                email: email,
                eventDateUtc: cartUpdatedAtUtc,
                eventProperties: eventProps,
                contactProperties: BuildEventContactProperties(user.FullName, user.PhoneNumber),
                ct: ct);

            _logger.LogInformation(
                "Brevo cart_abandoned sync finished for {Email}. ContactOk={ContactOk} EventOk={EventOk} ListId={ListId} Items={ItemCount}",
                email,
                contactOk,
                eventOk,
                _settings.CartAbandonedListId,
                itemCount);

            return contactOk || eventOk;
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

        private async Task<bool> TrackEventAsync(
            string eventName,
            string email,
            DateTime? eventDateUtc,
            Dictionary<string, object?> eventProperties,
            Dictionary<string, object?>? contactProperties,
            CancellationToken ct)
        {
            if (!_settings.Enabled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _logger.LogWarning("Brevo is enabled but ApiKey is empty. Skipping event {EventName} for {Email}", eventName, email);
                return false;
            }

            try
            {
                var payload = new Dictionary<string, object?>
                {
                    ["event_name"] = eventName,
                    ["identifiers"] = new Dictionary<string, object?>
                    {
                        ["email_id"] = email
                    },
                    ["event_properties"] = eventProperties,
                };

                if (contactProperties is { Count: > 0 })
                {
                    payload["contact_properties"] = contactProperties;
                }

                if (eventDateUtc.HasValue)
                {
                    payload["event_date"] = eventDateUtc.Value.ToString("O");
                }

                var req = new HttpRequestMessage(HttpMethod.Post, "events")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };

                req.Headers.Add("api-key", _settings.ApiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "Brevo event failed. Event={EventName} Status={Status} Email={Email} Body={Body}",
                        eventName,
                        (int)resp.StatusCode,
                        email,
                        body);
                    return false;
                }

                _logger.LogInformation("Brevo event posted. Event={EventName} Email={Email}", eventName, email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brevo event exception. Event={EventName} Email={Email}", eventName, email);
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

        private static Dictionary<string, object?> BuildEventContactProperties(string? fullName, string? phone)
        {
            var props = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                props["FULLNAME"] = fullName;
            }
            if (!string.IsNullOrWhiteSpace(phone))
            {
                props["PHONE"] = phone;
            }
            return props;
        }

        private static List<Dictionary<string, object?>> BuildOrderItems(IEnumerable<OrderItem>? items)
        {
            return items?
                .Select(item => new Dictionary<string, object?>
                {
                    ["product_id"] = item.ProductId,
                    ["product_object_id"] = item.ProductObjectId,
                    ["name"] = item.ProductName,
                    ["quantity"] = item.Quantity,
                    ["size"] = item.Size,
                    ["unit_price"] = item.UnitPrice,
                    ["line_total"] = item.LineTotal,
                    ["currency"] = item.Currency,
                    ["thumbnail_url"] = item.ThumbnailUrl,
                    ["fabric"] = item.Fabric,
                    ["color"] = item.Color,
                    ["monogram"] = item.Monogram
                })
                .ToList() ?? new List<Dictionary<string, object?>>();
        }

        private static List<Dictionary<string, object?>> BuildCartItems(IEnumerable<CartItem> items)
        {
            return items
                .Select(item => new Dictionary<string, object?>
                {
                    ["product_id"] = item.ProductId,
                    ["product_object_id"] = item.ProductObjectId,
                    ["name"] = item.ProductName,
                    ["variant_sku"] = item.VariantSku,
                    ["quantity"] = item.Quantity,
                    ["unit_price"] = item.Price,
                    ["line_total"] = item.Price * item.Quantity,
                    ["currency"] = item.Currency,
                    ["slug"] = item.Snapshot?.Slug,
                    ["thumbnail_url"] = item.Snapshot?.ThumbnailUrl,
                    ["variant_id"] = item.Snapshot?.VariantId,
                    ["variant_size"] = item.Snapshot?.VariantSize,
                    ["variant_color"] = item.Snapshot?.VariantColor,
                    ["product_url"] = string.IsNullOrWhiteSpace(item.Snapshot?.Slug) ? null : $"https://www.yobha.world/product-description/{item.Snapshot.Slug}"
                })
                .ToList();
        }
    }
}
