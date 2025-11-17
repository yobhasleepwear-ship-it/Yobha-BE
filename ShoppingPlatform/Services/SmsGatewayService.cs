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

            // message template
            var message = $"Dear Customer,\nYour one-time password (OTP) for login to YOBHA is {otp}.\nPlease do not share this OTP with anyone for security reasons.\n– YOBHA";

            // url encode message
            var encodedMessage = WebUtility.UrlEncode(message);

            // build url (as per provided spec). leave APIKey placeholder to be replaced by you
            // You mentioned you'll add secretkey where needed — kept as placeholder const above.
            //var url = $"https://www.smsgatewayhub.com/api/mt/SendSMS?APIKey={secretsDoc?.SMSAPIKEY}&senderid={_senderId}&channel=2&DCS=0&flashsms=0&number={phoneNumber}&text={encodedMessage}&route=1";
            var url = $"https://www.smsgatewayhub.com/api/mt/SendSMS?APIKey={secretsDoc?.SMSAPIKEY}&senderid={_senderId}&channel=2&DCS=0&flashsms=0&number={phoneNumber}&text={encodedMessage}&route=47&EntityId=1101481040000090255&dlttemplateid=1107176318996242909";
            var providerResult = new SmsProviderResult
            {
                IsSuccess = false
            };

            try
            {
                var resp = await _http.GetAsync(url, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);

                providerResult.RawResponse = raw;
                providerResult.ProviderStatus = resp.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)resp.StatusCode}";
                providerResult.IsSuccess = resp.IsSuccessStatusCode;

                // Some gateways return a message id in body. We cannot be sure of format, so just save raw.
                // If SMSGatewayHub returns something parseable, you can parse message id here:
                providerResult.ProviderMessageId = ExtractMessageIdFromRaw(raw);

                // SessionId: we can use a generated session id (or provider's id if available)
                providerResult.SessionId = Guid.NewGuid().ToString("N");

                _logger.LogInformation("Sent SMS via SMSGatewayHub phone={phoneMask} ok={ok} resp={resp}",
                    MaskPhone(phoneNumber), providerResult.IsSuccess, Truncate(raw, 200));
            }
            catch (Exception ex)
            {
                providerResult.IsSuccess = false;
                providerResult.ProviderStatus = "EXCEPTION";
                providerResult.RawResponse = ex.ToString();
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
