using DocumentFormat.OpenXml.Spreadsheet;
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

            // Exact working template (single line, no newline, no unicode dash)
            var message = $"Dear Customer, Your one-time password (OTP) for login to YOBHA website is {otp}. Please do not share this OTP with anyone for security reasons. -YOBHA";

            // URL encode text
            var encodedMessage = Uri.EscapeDataString(message);

            var normalizedNumber = phoneNumber?.Trim() ?? "";
            //if (!normalizedNumber.StartsWith("91") && normalizedNumber.Length == 10)
            //    normalizedNumber = "91" + normalizedNumber;

            var apiKey = secretsDoc?.SMSAPIKEY ?? "";
            var entityId = "1101481040000090255";
            var dlttemplateid = "1107176318996242909";

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

            _logger.LogInformation("DEBUG SMS message url: [{url}]" + url);

            var providerResult = new SmsProviderResult { IsSuccess = false };

            try
            {
                var resp = await _http.GetAsync(url, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                providerResult.RawResponse = raw;

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
            }
            catch (Exception ex)
            {
                providerResult.IsSuccess = false;
                providerResult.ProviderStatus = "EXCEPTION";
                providerResult.RawResponse = ex.ToString();
                providerResult.SessionId = Guid.NewGuid().ToString("N");
            }

            return (providerResult, otp);
        }


        public async Task<SmsProviderResult> SendOrderConfirmationSmsAsync(
            string phoneNumber,
            string customerName,
            string orderId,
            string amount,
            CancellationToken ct = default)
        {
            var message =
                $"Dear {customerName} your order {orderId} has been successfully placed. Order amount: Rs. {amount}. -YOBHA";

            return await SendTemplateSmsAsync(
                phoneNumber,
                message,
                "1107176984040693504", // Order Confirmation DLT template ID
                ct);
        }




        public async Task<SmsProviderResult> SendAdminNewOrderSmsAsync(
    string phoneNumber,
    string orderId,
    string customerName,
    string amount,
    CancellationToken ct = default)
        {
            var message =
                $"New order received. Order ID: {orderId}, Customer: {customerName}, Amount: Rs. {amount}. Please check the admin panel for details. -YOBHA";

            return await SendTemplateSmsAsync(
                "916239713989",
                message,
                "1107176984083352336",
                ct);
        }

        public async Task<SmsProviderResult> SendReturnInitiatedSmsAsync(
    string phoneNumber,
    string customerName,
    string orderId,
    CancellationToken ct = default)
        {
            var message =
                $"Dear {customerName}, return request for order {orderId} has been initiated. -YOBHA";

            return await SendTemplateSmsAsync(
                phoneNumber,
                message,
                "1107176984074904642",
                ct);
        }


        public async Task<SmsProviderResult> SendOrderCancellationSmsAsync(
    string phoneNumber,
    string customerName,
    string orderId,
    CancellationToken ct = default)
        {
            var message =
                $"Dear {customerName}, your order {orderId} has been cancelled successfully. -YOBHA";

            return await SendTemplateSmsAsync(
                phoneNumber,
                message,
                "1107176984049785231",
                ct);
        }

        private async Task<SmsProviderResult> SendTemplateSmsAsync(
    string phoneNumber,
    string message,
    string dlttemplateid,
    CancellationToken ct = default)
        {
            var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync("OTP");

            // URL encode message (DLT safe)
            var encodedMessage = Uri.EscapeDataString(message);

            // Normalize Indian mobile number
            var normalizedNumber = phoneNumber?.Trim() ?? "";
            //if (!normalizedNumber.StartsWith("91") && normalizedNumber.Length == 10)
            //    normalizedNumber = "91" + normalizedNumber;

            var apiKey = secretsDoc?.SMSAPIKEY ?? "";
            var entityId = "1101481040000090255";

            var url =
                $"https://www.smsgatewayhub.com/api/mt/SendSMS" +
                $"?APIKey={Uri.EscapeDataString(apiKey)}" +
                $"&senderid={Uri.EscapeDataString(_senderId)}" +
                $"&channel=2" +
                $"&DCS=0" +
                $"&flashsms=0" +
                $"&number={Uri.EscapeDataString(normalizedNumber)}" +
                $"&text={encodedMessage}" +
                $"&route=1" +
                $"&EntityId={Uri.EscapeDataString(entityId)}" +
                $"&dlttemplateid={Uri.EscapeDataString(dlttemplateid)}";

            _logger.LogInformation("DEBUG SMS message url: [{url}]", url);

            var providerResult = new SmsProviderResult { IsSuccess = false };

            try
            {
                var resp = await _http.GetAsync(url, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogInformation(
    "DEBUG SMS response: StatusCode={StatusCode}, Response={Response}",
    (int)resp.StatusCode,
    raw
);

                providerResult.RawResponse = raw;

                try
                {
                    var j = JObject.Parse(raw);

                    var errorCode = j["ErrorCode"]?.ToString();
                    providerResult.ProviderStatus =
                        j["ErrorMessage"]?.ToString()
                        ?? (resp.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)resp.StatusCode}");

                    providerResult.IsSuccess =
                        string.Equals(errorCode, "000", StringComparison.OrdinalIgnoreCase);

                    var messageData = j["MessageData"] as JArray;
                    if (messageData != null && messageData.Count > 0)
                        providerResult.ProviderMessageId =
                            messageData[0]?["MessageId"]?.ToString();

                    providerResult.SessionId =
                        providerResult.ProviderMessageId
                        ?? Guid.NewGuid().ToString("N");
                }
                catch
                {
                    // Fallback if response is not JSON
                    providerResult.ProviderStatus =
                        resp.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)resp.StatusCode}";
                    providerResult.IsSuccess = resp.IsSuccessStatusCode;
                    providerResult.SessionId = Guid.NewGuid().ToString("N");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DEBUG SMS response: Exception occurred");


                providerResult.IsSuccess = false;
                providerResult.ProviderStatus = "EXCEPTION";
                providerResult.RawResponse = ex.ToString();
                providerResult.SessionId = Guid.NewGuid().ToString("N");
            }

            return providerResult;
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
