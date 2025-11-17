using Newtonsoft.Json.Linq;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Repositories;
using System.Net;

namespace ShoppingPlatform.Services
{
    public class SmsGatewayService : ISmsGatewayService
    {
        private readonly HttpClient _http;
        private readonly ILogger<SmsGatewayService> _logger;
        private readonly string _senderId = "YOBHAA"; // or put in config
        private readonly ISecretsRepository _secretsRepo;


        public SmsGatewayService(HttpClient http, ILogger<SmsGatewayService> logger , ISecretsRepository secretsRepo)
        {
            _http = http;
            _logger = logger;
            _secretsRepo = secretsRepo ?? throw new ArgumentNullException(nameof(secretsRepo));
        }

        public async Task<(SmsProviderResult providerResult, string otp)> SendOtpAsync(string phoneNumber, CancellationToken ct = default)
        {
            var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync("OTP");

            // generate 6-digit OTP
            var rng = new Random();
            var otp = rng.Next(100000, 999999).ToString();

            // Exact template — **match DLT**: no extra spaces after en-dash
            var template = "Dear Customer,\nYour one-time password (OTP) for login to YOBHA is {0}.\nPlease do not share this OTP with anyone for security reasons.\n–YOBHA";
            var message = string.Format(template, otp);

            // DEBUG (dev only): dump the message to logs (remove in prod)
            _logger.LogDebug("DEBUG SMS message (before encoding): [{msg}]", message);

            // Use Uri.EscapeDataString to preserve Unicode characters (en-dash) and encode newline as %0A
            var encodedMessage = Uri.EscapeDataString(message);

            var normalizedNumber = phoneNumber?.Trim() ?? "";
            if (!normalizedNumber.StartsWith("91") && normalizedNumber.Length == 10)
                normalizedNumber = "91" + normalizedNumber;

            var apiKey = secretsDoc?.SMSAPIKEY ?? "";
            var entityId =  "1101481040000090255";
            var dlttemplateid =  "1107176318996242909";

            var url =
                $"https://www.smsgatewayhub.com/api/mt/SendSMS" +
                $"?APIKey={Uri.EscapeDataString(apiKey)}" +
                $"&senderid={Uri.EscapeDataString(_senderId)}" +
                $"&channel=2&DCS=0&flashsms=0" +
                $"&number={Uri.EscapeDataString(normalizedNumber)}" +
                $"&text={encodedMessage}" +
                $"&route=1" +
                $"&EntityId={Uri.EscapeDataString(entityId)}" +
                $"&dlttemplateid={Uri.EscapeDataString(dlttemplateid)}";

            _logger.LogDebug("DEBUG SMS message url: [{url}]", url);

            var providerResult = new SmsProviderResult { IsSuccess = false };

            try
            {
                var resp = await _http.GetAsync(url, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("DEBUG SMS message raw: [{raw}]", raw);
                providerResult.RawResponse = raw;

                // parse JSON response and treat ErrorCode == "000" as success
                try
                {
                    var j = JObject.Parse(raw);
                    var errorCode = j["ErrorCode"]?.ToString();
                    providerResult.ProviderStatus = j["ErrorMessage"]?.ToString() ?? (resp.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)resp.StatusCode}");
                    providerResult.IsSuccess = string.Equals(errorCode, "000", StringComparison.OrdinalIgnoreCase);

                    var messageData = j["MessageData"] as JArray;
                    if (messageData != null && messageData.Count > 0)
                        providerResult.ProviderMessageId = messageData[0]?["MessageId"]?.ToString();

                    providerResult.SessionId = providerResult.ProviderMessageId ?? Guid.NewGuid().ToString("N");
                }
                catch
                {
                    providerResult.ProviderStatus = resp.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)resp.StatusCode}";
                    providerResult.IsSuccess = resp.IsSuccessStatusCode;
                    providerResult.SessionId = Guid.NewGuid().ToString("N");
                }

                _logger.LogInformation("SMS send attempt for {phoneMask} accepted={accepted} status={status} msgId={mid}",
                    MaskPhone(normalizedNumber), providerResult.IsSuccess, providerResult.ProviderStatus, providerResult.ProviderMessageId);
            }
            catch (Exception ex)
            {
                providerResult.IsSuccess = false;
                providerResult.ProviderStatus = "EXCEPTION";
                providerResult.RawResponse = ex.ToString();
                providerResult.SessionId = Guid.NewGuid().ToString("N");
                _logger.LogError(ex, "Error calling SMSGatewayHub for {phone}", phoneNumber);
            }

            return (providerResult, otp);
        }

        private string? ExtractMessageIdFromRaw(string raw)
        {
            // implement parsing if you know SMSGatewayHub response format.
            // return null for now.
            return null;
        }

        // small helpers you'd already have in your codebase:
        private string MaskPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length < 4) return phone;
            return new string('*', phone.Length - 4) + phone.Substring(phone.Length - 4);
        }

        private string Truncate(string? s, int max = 1000) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max);
    }
}
