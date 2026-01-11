using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Repositories;

namespace ShoppingPlatform.Services
{
    public class DelhiveryDeliveryService : IDeliveryService
    {
        private readonly HttpClient _httpClient;
        private readonly ISecretsRepository _secretsRepository;

        private string? _apiToken;
        private string? _pickupLocation;
        private readonly ILogger<DelhiveryDeliveryService> _logger;

        public DelhiveryDeliveryService(
            HttpClient httpClient,
            ISecretsRepository secretsRepository,
            ILogger<DelhiveryDeliveryService> logger)
        {
            _httpClient = httpClient;
            _secretsRepository = secretsRepository;
            _logger = logger;
        }

        // 🔐 Load secrets once (lazy + cached)
        private async Task EnsureSecretsLoadedAsync()
        {
            if (!string.IsNullOrEmpty(_apiToken))
            {
                _logger.LogDebug("Delhivery secrets already loaded (cached)");
                return;
            }

            _logger.LogInformation("Loading Delhivery secrets from database");

            var secrets = await _secretsRepository.GetSecretsByAddedForAsync("DELHIVERY");

            if (secrets == null)
            {
                _logger.LogError("Delhivery secrets document not found");
                throw new Exception("Delhivery secrets not found");
            }

            if (string.IsNullOrEmpty(secrets.DelhiveryApiToken))
            {
                _logger.LogError("Delhivery API token missing in secrets");
                throw new Exception("Delhivery API token not configured");
            }

            if (string.IsNullOrEmpty(secrets.DelhiveryPickupLocation))
            {
                _logger.LogError("Delhivery pickup location missing in secrets");
                throw new Exception("Delhivery pickup location not configured");
            }

            _apiToken = secrets.DelhiveryApiToken;
            _pickupLocation = secrets.DelhiveryPickupLocation;

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Token", _apiToken);

            _logger.LogInformation(
                "Delhivery secrets loaded successfully. PickupLocation={PickupLocation}",
                _pickupLocation
            );
        }


        // 🌍 INTERNATIONAL
        public async Task<string> CreateInternationalShipmentAsync(InternationalDeliveryRequest request)
        {
            await EnsureSecretsLoadedAsync();

            _logger.LogInformation(
                "Creating INTERNATIONAL shipment | OrderId={OrderId}, Country={Country}",
                request.OrderId,
                request.CountryCode
            );

            var payload = new
            {
                order_id = request.OrderId,
                destination_country = request.CountryCode,
                commodity = request.Commodity,
                consignee = new
                {
                    name = request.Name,
                    address = request.Address
                },
                package = new
                {
                    weight = request.Weight,
                    value = request.Value,
                    currency = request.Currency
                }
            };

            _logger.LogDebug(
                "Delhivery INTL JSON payload: {Payload}",
                JsonConvert.SerializeObject(payload)
            );

            string url = "https://track.delhivery.com/api/international/create";

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(
                "Delhivery INTL response | Status={Status} | Body={Body}",
                response.StatusCode,
                responseBody
            );

            if (!response.IsSuccessStatusCode)
                throw new Exception($"International shipment failed: {responseBody}");

            var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (parsed?.success != true)
                throw new Exception($"International shipment failed: {responseBody}");

            return parsed.awb;
        }


        // 🇮🇳 DOMESTIC (FORWARD + REVERSE)
        public async Task<string> CreateDomesticShipmentAsync(DomesticShipmentRequest request)
        {
            await EnsureSecretsLoadedAsync();

            var shipment = new
            {
                order = request.OrderId,
                is_reverse = request.IsReverse,

                name = request.PickupName,
                phone = request.PickupPhone,
                add = request.PickupAddress,
                pin = request.PickupPincode,

                return_name = request.DropName,
                return_phone = request.DropPhone,
                return_add = request.DropAddress,
                return_pin = request.DropPincode,

                payment_mode = request.IsCod ? "COD" : "Prepaid",
                cod_amount = request.IsCod ? request.CodAmount : 0,

                weight = request.Weight
            };

            var formData = new Dictionary<string, string>
    {
        { "format", "json" }, // 🔥 THIS IS WHAT DELHIVERY READS
        { "shipments", JsonConvert.SerializeObject(new[] { shipment }) },
        { "pickup_location", JsonConvert.SerializeObject(new { name = _pickupLocation }) }
    };

            var responseBody = await PostFormAsync("/api/cmu/create.json", formData);

            var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (parsed?.success != true)
                throw new Exception($"Domestic shipment failed: {responseBody}");

            return parsed.packages[0].waybill;
        }

        // 📦 TRACKING
        public async Task<string> TrackShipmentAsync(string awb)
        {
            await EnsureSecretsLoadedAsync();

            var response = await _httpClient.GetAsync(
                $"/api/v1/packages/json/?waybill={awb}");

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        // ❌ CANCEL SHIPMENT
        public async Task CancelShipmentAsync(string awb)
        {
            await EnsureSecretsLoadedAsync();

            _logger.LogInformation("Cancelling shipment | AWB={Awb}", awb);

            var formData = new Dictionary<string, string>
    {
        { "waybill", awb },
        { "cancellation", "true" }
    };

            string url = "https://track.delhivery.com/api/p/edit";

            _logger.LogDebug(
                "Delhivery CANCEL FORM payload: {@Payload}",
                formData
            );

            var content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(
                "Delhivery CANCEL response | Status={Status} | Body={Body}",
                response.StatusCode,
                responseBody
            );

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Cancel shipment failed: {responseBody}");
        }


        // 🔧 COMMON POST HANDLER
        private async Task<string> PostFormAsync(string url, Dictionary<string, string> formData)
        {
            string APIurl = "https://track.delhivery.com" + url;

            _logger.LogInformation("Calling Delhivery FORM API: {Url}", APIurl);
            _logger.LogDebug("Delhivery FORM Payload: {@Payload}", formData);

            var content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.PostAsync(APIurl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(
                "Delhivery FORM Response | Status={Status} | Body={Body}",
                response.StatusCode,
                responseBody
            );

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Delhivery API error: {responseBody}");

            return responseBody;
        }


        public async Task SchedulePickupAsync(string awb)
        {
            await EnsureSecretsLoadedAsync();

            _logger.LogInformation("Scheduling pickup for AWB={Awb}", awb);

            var formData = new Dictionary<string, string>
    {
        { "waybill", awb },
        { "pickup_date", DateTime.UtcNow.ToString("yyyy-MM-dd") }
    };

            _logger.LogDebug(
                "Delhivery PICKUP FORM payload: {@Payload}",
                formData
            );

            string url = "https://track.delhivery.com/api/p/pickup";

            var content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(
                "Delhivery PICKUP response | Status={Status} | Body={Body}",
                response.StatusCode,
                responseBody
            );

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Pickup scheduling failed: {responseBody}");

            _logger.LogInformation(
                "Pickup scheduled successfully for AWB={Awb}",
                awb
            );
        }



    }
}
