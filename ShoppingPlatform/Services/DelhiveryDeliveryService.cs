using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using MongoDB.Driver;
using Newtonsoft.Json;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
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
        private readonly IMongoCollection<Counter> _counters;

        public DelhiveryDeliveryService(
            HttpClient httpClient,
            ISecretsRepository secretsRepository,
            ILogger<DelhiveryDeliveryService> logger, IMongoDatabase db)
        {
            _httpClient = httpClient;
            _secretsRepository = secretsRepository;
            _logger = logger;
            _counters = db.GetCollection<Counter>("counters");

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
        public async Task<string> CreateInternationalShipmentAsync(
            InternationalDeliveryRequest request)
        {
            await EnsureSecretsLoadedAsync();

            var internalShipmentId = request.OrderId;//await GenerateInternalShipmentIdAsync();

            var payload = new
            {
                pickup_location = new
                {
                    name = _pickupLocation
                },
                shipments = new[]
                {
            new
            {
                order = internalShipmentId,
                //shipment_type = "International",

                consignee_name = request.Name,
                consignee_address = request.Address,
                add = request.Address,
                //pin = request.DropPinCode,
                phone = request.DropPhone,

                destination_country = request.CountryCode,
                commodity = request.Commodity,

                weight = request.Weight.ToString(),
                declared_value = request.Value.ToString(),
                currency = request.Currency,

                payment_mode = "Prepaid"
            }
        }
            };

            var formData = new Dictionary<string, string>
    {
        { "format", "json" },
        { "data", JsonConvert.SerializeObject(payload) }
    };

            var responseBody = await PostFormAsync(
                "/api/cmu/create.json",
                formData
            );

            if (responseBody.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Delhivery returned HTML – invalid endpoint or token");

            var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (parsed?.success != true)
                throw new Exception($"INTL shipment failed: {responseBody}");

            return (string)parsed.packages[0].waybill;
        }

        public async Task<string> CreateDomesticShipmentAsync(DomesticShipmentRequest request)
        {
            await EnsureSecretsLoadedAsync();

            var payload = new
            {
                pickup_location = new
                {
                    name = _pickupLocation // must exist in Delhivery panel
                },
                shipments = new[]
                {
            new
            {
                order = request.OrderId,          // YOUR internal order id
                weight = request.Weight.ToString(),
                pin = request.DropPincode,
                products_desc = "Clothes",
                add = request.DropAddress,
                state = request.DropState,
                city = request.DropCity,
                phone = request.DropPhone,
                payment_mode = request.IsCod ? "COD" : "Prepaid",
                name = request.DropName,
                total_amount = request.Amount,
                country = "India",
                cod_amount = request.CodAmount 
            }
        }
            };

            var formData = new Dictionary<string, string>
    {
        { "format", "json" },
        { "data", JsonConvert.SerializeObject(payload) }
    };

            var responseBody = await PostFormAsync("/api/cmu/create.json", formData);

            var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (parsed?.success != true)
                throw new Exception($"Domestic shipment failed: {responseBody}");

            // ✅ THIS is the ONLY valid waybill
            return parsed.packages[0].waybill;
        }


        // 🇮🇳 DOMESTIC (FORWARD + REVERSE)
        //    public async Task<string> CreateDomesticShipmentAsync(DomesticShipmentRequest request)
        //    {
        //        await EnsureSecretsLoadedAsync();

        //        var shipment = new
        //        {
        //            order = request.OrderId,
        //            is_reverse = request.IsReverse,

        //            name = request.PickupName,
        //            phone = request.PickupPhone,
        //            add = request.PickupAddress,
        //            pin = request.PickupPincode,

        //            return_name = request.DropName,
        //            return_phone = request.DropPhone,
        //            return_add = request.DropAddress,
        //            return_pin = request.DropPincode,

        //            payment_mode = request.IsCod ? "COD" : "Prepaid",
        //            cod_amount = request.IsCod ? request.CodAmount : 0,

        //            weight = request.Weight
        //        };

        //        var formData = new Dictionary<string, string>
        //{
        //    { "format", "json" }, // 🔥 THIS IS WHAT DELHIVERY READS
        //    { "shipments", JsonConvert.SerializeObject(new[] { shipment }) },
        //    { "pickup_location", JsonConvert.SerializeObject(new { name = _pickupLocation }) }
        //};

        //        var responseBody = await PostFormAsync("/api/cmu/create.json", formData);

        //        var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

        //        if (parsed?.success != true)
        //            throw new Exception($"Domestic shipment failed: {responseBody}");

        //        return parsed.packages[0].waybill;
        //    }

        // 📦 TRACKING

        public async Task<string> GenerateInternalShipmentIdAsync()
        {
            var filter = Builders<Counter>.Filter.Eq(x => x.CounterFor, "DOMESTIC_SHIPMENT");

            var update = Builders<Counter>.Update.Inc(x => x.Seq, 1);

            var options = new FindOneAndUpdateOptions<Counter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var counter = await _counters.FindOneAndUpdateAsync(
                filter,
                update,
                options
            );

            // Example: SHP-00000123
            return $"SHP-{counter.Seq:D8}";
        }

        public async Task<string> TrackShipmentAsync(string awb)
        {
            await EnsureSecretsLoadedAsync();

            var url =
                $"https://track.delhivery.com/api/v1/packages/json/?waybill={awb}";

            using var request = CreateDelhiveryRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Tracking failed: {body}");

            return body;
        }

        // ❌ CANCEL SHIPMENT
        public async Task CancelShipmentAsync(string awb)
        {
            await EnsureSecretsLoadedAsync();

            var url = "https://track.delhivery.com/api/p/edit";

            var formData = new Dictionary<string, string>
    {
        { "waybill", awb },
        { "cancellation", "true" }
    };

            using var request = CreateDelhiveryRequest(HttpMethod.Post, url);
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Cancel shipment failed: {body}");
        }


        // 🔧 COMMON POST HANDLER
        private async Task<string> PostFormAsync(
            string url,
            Dictionary<string, string> formData)
        {
            await EnsureSecretsLoadedAsync();

            string apiUrl = "https://track.delhivery.com" + url;

            _logger.LogInformation("Calling Delhivery FORM API: {Url}", apiUrl);
            _logger.LogDebug("Delhivery FORM Payload: {@Payload}", formData);

            using var request = CreateDelhiveryRequest(HttpMethod.Post, apiUrl);
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);
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

            var url = "https://track.delhivery.com/api/p/pickup";

            var formData = new Dictionary<string, string>
    {
        { "waybill", awb },
        { "pickup_date", DateTime.UtcNow.ToString("yyyy-MM-dd") }
    };

            using var request = CreateDelhiveryRequest(HttpMethod.Post, url);
            request.Content = new FormUrlEncodedContent(formData);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Pickup scheduling failed: {body}");
        }

        private HttpRequestMessage CreateDelhiveryRequest(
    HttpMethod method,
    string url)
        {
            var request = new HttpRequestMessage(method, url);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Token", _apiToken);

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );

            return request;
        }


        public async Task<DelhiveryPincodeResponse> CheckPincodeServiceabilityAsync(string pincode)
        {
            await EnsureSecretsLoadedAsync();

            var url =
                $"https://track.delhivery.com/c/api/pin-codes/json/?filter_codes={pincode}";

            _logger.LogInformation(
                "Checking Delhivery pincode serviceability | Pincode={Pincode}",
                pincode
            );

            using var request = CreateDelhiveryRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(
                "Delhivery PINCODE response | Status={Status} | Body={Body}",
                response.StatusCode,
                responseBody
            );

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Pincode check failed: {responseBody}");

            var parsed =
                JsonConvert.DeserializeObject<DelhiveryPincodeResponse>(responseBody);

            return parsed!;
        }



    }
}
