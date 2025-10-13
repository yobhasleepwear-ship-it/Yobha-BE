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
        public string id { get; set; } = null!;
        public int amount { get; set; }
        public string currency { get; set; } = null!;
        public string status { get; set; } = null!;
        public string receipt { get; set; } = null!;
    }

    public interface IRazorpayService
    {
        Task<RazorpayOrderResponse> CreateOrderAsync(decimal amountDecimal, string currency, string receipt);
        bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
        bool VerifyWebhookSignature(string body, string signatureHeader, string webhookSecret);
        string KeyId { get; }
    }

    public class RazorpayService : IRazorpayService
    {
        private readonly HttpClient _http;
        private readonly string _keyId;
        private readonly string _keySecret;
        private readonly string _webhookSecret;

        public string KeyId => _keyId;

        public RazorpayService(IConfiguration config, HttpClient httpClient)
        {
            _http = httpClient;
            _keyId = config["Razorpay:KeyId"] ?? throw new ArgumentNullException("Razorpay:KeyId");
            _keySecret = config["Razorpay:KeySecret"] ?? throw new ArgumentNullException("Razorpay:KeySecret");
            _webhookSecret = config["Razorpay:WebhookSecret"] ?? ""; // optional
            var byteArray = Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            _http.BaseAddress = new Uri("https://api.razorpay.com/v1/");
        }

        public async Task<RazorpayOrderResponse> CreateOrderAsync(decimal amountDecimal, string currency, string receipt)
        {
            // Razorpay expects amount in smallest currency unit (paise)
            var amountInPaise = (int)Math.Round(amountDecimal * 100M, 0, MidpointRounding.AwayFromZero);

            var payload = new
            {
                amount = amountInPaise,
                currency = currency,
                receipt = receipt,
                payment_capture = 1 // auto-capture
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("orders", content);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            var orderResp = JsonConvert.DeserializeObject<RazorpayOrderResponse>(body)!;
            return orderResp;
        }

        // Verify signature from client after checkout: signature = HMAC_SHA256(orderId + "|" + paymentId, keySecret)
        public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
        {
            var payload = $"{orderId}|{paymentId}";
            var hash = ComputeHmacSha256(payload, _keySecret);
            return hash == signature;
        }

        public bool VerifyWebhookSignature(string body, string signatureHeader, string webhookSecret)
        {
            if (string.IsNullOrEmpty(webhookSecret)) return false;
            var hash = ComputeHmacSha256(body, webhookSecret);
            return hash == signatureHeader;
        }

        private static string ComputeHmacSha256(string text, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var textBytes = Encoding.UTF8.GetBytes(text);
            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(textBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
