// File: Sms/TwoFactorService.cs
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShoppingPlatform.DTOs;
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShoppingPlatform.Sms
{
    public class TwoFactorService
    {
        private readonly HttpClient _http;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(HttpClient http, ILogger<TwoFactorService> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // POST template SMS and return ProviderResult
        public async Task<ProviderResult> SendOtpAsync(
            string apiKey,
            string phoneNumber,
            string? senderId = "YOBHAS",
            string templateName = "SENDOTP",   // your approved template name
            string? var1 = null,
            string? var2 = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException(nameof(apiKey));
            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new ArgumentException(nameof(phoneNumber));

            // --- Helper: normalize to Indian format (91XXXXXXXXXX)
            static string NormalizePhone(string phone)
            {
                var digits = Regex.Replace(phone ?? string.Empty, @"\D", "");
                if (digits.Length == 10) return "91" + digits;
                if (digits.Length == 11 && digits.StartsWith("0")) return "91" + digits.Substring(1);
                if (digits.StartsWith("91") && digits.Length >= 12) return digits;
                return digits;
            }

            var normalizedPhone = NormalizePhone(phoneNumber);

            // --- Helper: mask API key for logs
            static string MaskKey(string s, int showRight = 4)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                if (s.Length <= showRight) return new string('*', s.Length);
                return new string('*', s.Length - showRight) + s[^showRight..];
            }

            // --- Build form fields
            var form = new List<KeyValuePair<string, string>>
    {
        new KeyValuePair<string, string>("From", senderId ?? "YOBHAS"),
        new KeyValuePair<string, string>("To", normalizedPhone),
    };

            if (!string.IsNullOrWhiteSpace(templateName))
                form.Add(new KeyValuePair<string, string>("TemplateName", templateName));

            if (!string.IsNullOrWhiteSpace(var1))
                form.Add(new KeyValuePair<string, string>("VAR1", var1!));

            if (!string.IsNullOrWhiteSpace(var2))
                form.Add(new KeyValuePair<string, string>("VAR2", var2!));

            // --- URL: use normalizedPhone (important)
            var url = $"https://2factor.in/API/V1/{apiKey}/SMS/{normalizedPhone}/AUTOGEN/{senderId}";

            _logger.LogInformation("TwoFactor.SendOtp -> sending to {to} template={template} apiKey={keyMasked}",
                normalizedPhone, templateName, MaskKey(apiKey));

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await _http.SendAsync(request);
            var body = await resp.Content.ReadAsStringAsync();

            _logger.LogInformation("TwoFactor response HTTP {code} bodyLength={len}", (int)resp.StatusCode, body?.Length ?? 0);
            _logger.LogDebug("TwoFactor response body: {body}", body);

            var result = new ProviderResult { RawResponse = body ?? string.Empty };

            if (!resp.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ProviderStatus = $"HTTP_{(int)resp.StatusCode}";
                return result;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<JObject?>(body ?? "{}");
                var status = parsed?["Status"]?.ToString() ?? parsed?["status"]?.ToString() ?? string.Empty;
                var details = parsed?["Details"]?.ToString() ?? parsed?["details"]?.ToString() ?? string.Empty;

                result.ProviderStatus = status;
                result.SessionId = details;
                result.ProviderMessageId = parsed?["MessageId"]?.ToString() ?? string.Empty;

                result.IsSuccess = string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TwoFactor: failed to parse provider response");
                result.IsSuccess = false;
                result.ProviderStatus = "PARSE_ERROR";
                return result;
            }
        }



        // Verify OTP
        public async Task<bool> VerifyOtpAsync(string apiKey, string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId");
            if (string.IsNullOrWhiteSpace(otp)) throw new ArgumentException("otp");

            var url = $"https://2factor.in/API/V1/{apiKey}/SMS/VERIFY/{sessionId}/{otp}";
            _logger.LogInformation("TwoFactor.VerifyOtp -> session={session}", sessionId);

            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogDebug("TwoFactor.VerifyOtp response: {body}", body);

            if (!resp.IsSuccessStatusCode) return false;

            try
            {
                dynamic parsed = JsonConvert.DeserializeObject(body);
                string status = parsed?.Status ?? string.Empty;
                return string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> VerifyOtpAsyncV2(string apiKey, string sessionId, string otp)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("apiKey");
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("sessionId");
            if (string.IsNullOrWhiteSpace(otp)) throw new ArgumentException("otp");

            // NOTE: "sessionId" here should be the transactionId you saved when generating the OTP.
            _logger.LogInformation("SMSGatewayHub.VerifyOtp -> transaction={transactionId}", sessionId);

            var candidateUrls = new[]
            {
        // common patterns observed across providers and SMSGatewayHub docs (adjust if your dashboard shows a different one)
        $"https://www.smsgatewayhub.com/api/otp/verify?APIKey={Uri.EscapeDataString(apiKey)}&TransactionId={Uri.EscapeDataString(sessionId)}&OTP={Uri.EscapeDataString(otp)}",
        $"https://www.smsgatewayhub.com/api/otp/Verify?APIKey={Uri.EscapeDataString(apiKey)}&TransactionId={Uri.EscapeDataString(sessionId)}&OTP={Uri.EscapeDataString(otp)}",
        // fallback pattern where parameter names are transactionId/otp (lowercase)
        $"https://www.smsgatewayhub.com/api/otp/verify?APIKey={Uri.EscapeDataString(apiKey)}&transactionId={Uri.EscapeDataString(sessionId)}&otp={Uri.EscapeDataString(otp)}",
        // a generic verify endpoint (some panels use /OTPApi/Verify or similar)
        $"https://www.smsgatewayhub.com/OTPApi/Verify?APIKey={Uri.EscapeDataString(apiKey)}&transactionId={Uri.EscapeDataString(sessionId)}&otp={Uri.EscapeDataString(otp)}"
    };

            foreach (var url in candidateUrls)
            {
                HttpResponseMessage resp;
                string body = null;

                try
                {
                    resp = await _http.GetAsync(url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SMSGatewayHub.VerifyOtp -> HTTP call failed for url {url}", url);
                    continue; // try next pattern
                }

                try
                {
                    body = await resp.Content.ReadAsStringAsync();
                    _logger.LogDebug("SMSGatewayHub.VerifyOtp response from {url}: {body}", url, body);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SMSGatewayHub.VerifyOtp -> failed reading response from {url}", url);
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMSGatewayHub.VerifyOtp -> non-success status {status} from {url}", resp.StatusCode, url);
                    // still try next candidate; sometimes providers return 200 with failure info
                }

                // Try to parse known response shapes
                try
                {
                    // pattern 1: { "ErrorCode":"000", "ErrorMessage":"Success", ... }
                    var r1 = JsonConvert.DeserializeObject<SmsGatewayVerifyResponse1>(body);
                    if (r1 != null && string.Equals(r1.ErrorCode, "000", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("SMSGatewayHub.VerifyOtp -> success via ErrorCode on {url}", url);
                        return true;
                    }

                    if (r1 != null && !string.IsNullOrWhiteSpace(r1.ErrorMessage) &&
                        string.Equals(r1.ErrorMessage, "Success", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("SMSGatewayHub.VerifyOtp -> success via ErrorMessage on {url}", url);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SMSGatewayHub.VerifyOtp -> pattern1 parse error");
                }

                try
                {
                    // pattern 2: { "statusCode": 900, "status": "...", "transactionId": "..." }
                    var r2 = JsonConvert.DeserializeObject<SmsGatewayVerifyResponse2>(body);
                    if (r2 != null)
                    {
                        if (r2.statusCode.HasValue && r2.statusCode.Value == 900)
                        {
                            _logger.LogInformation("SMSGatewayHub.VerifyOtp -> success via statusCode==900 on {url}", url);
                            return true;
                        }

                        if (!string.IsNullOrWhiteSpace(r2.status) && string.Equals(r2.status, "Success", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("SMSGatewayHub.VerifyOtp -> success via status=='Success' on {url}", url);
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SMSGatewayHub.VerifyOtp -> pattern2 parse error");
                }

                // last-resort: check if any property contains 'Success' text
                if (!string.IsNullOrWhiteSpace(body) && body.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogInformation("SMSGatewayHub.VerifyOtp -> heuristically treating response as success from {url}", url);
                    return true;
                }

                // else try next candidate
            }

            _logger.LogWarning("SMSGatewayHub.VerifyOtp -> all verify attempts failed for transaction {transactionId}", sessionId);
            return false;
        }
    }
}
