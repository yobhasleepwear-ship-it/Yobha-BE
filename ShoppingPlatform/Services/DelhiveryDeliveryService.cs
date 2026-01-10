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

        public DelhiveryDeliveryService(
            HttpClient httpClient,
            ISecretsRepository secretsRepository)
        {
            _httpClient = httpClient;
            _secretsRepository = secretsRepository;
        }

        // 🔐 Load secrets once (lazy + cached)
        private async Task EnsureSecretsLoadedAsync()
        {
            if (!string.IsNullOrEmpty(_apiToken))
                return;

            var secrets = await _secretsRepository.GetSecretsByAddedForAsync("DELHIVERY");

            if (secrets == null || string.IsNullOrEmpty(secrets.DelhiveryApiToken))
                throw new Exception("Delhivery API token not found in Secrets collection");

            _apiToken = secrets.DelhiveryApiToken;
            _pickupLocation = secrets.DelhiveryPickupLocation;

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Token", _apiToken);
        }

        // 🌍 INTERNATIONAL
        public async Task<string> CreateInternationalShipmentAsync(InternationalDeliveryRequest request)
        {
            await EnsureSecretsLoadedAsync();

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

            var responseBody = await PostAsync("/api/international/create", payload);
            var parsed = JsonConvert.DeserializeObject<dynamic>(responseBody);

            if (parsed?.success != true)
                throw new Exception($"International shipment failed: {responseBody}");

            return parsed.awb;
        }

        // 🇮🇳 DOMESTIC (FORWARD + REVERSE)
        public async Task<string> CreateDomesticShipmentAsync(DomesticShipmentRequest request)
        {
            await EnsureSecretsLoadedAsync();

            var payload = new
            {
                shipments = new[]
                {
                    new
                    {
                        order = request.OrderId,
                        is_reverse = request.IsReverse,

                        // Pickup
                        name = request.PickupName,
                        phone = request.PickupPhone,
                        add = request.PickupAddress,
                        pin = request.PickupPincode,

                        // Drop
                        return_name = request.DropName,
                        return_phone =   request.DropPhone,
                        return_add = request.DropAddress,
                        return_pin = request.DropPincode,

                        payment_mode = request.IsCod ? "COD" : "Prepaid",
                        cod_amount = request.IsCod ? request.CodAmount : 0,

                        weight = request.Weight
                    }
                },
                pickup_location = new
                {
                    name = _pickupLocation
                }
            };

            var responseBody = await PostAsync("/api/cmu/create.json", payload);
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

            var payload = new { waybill = awb, cancellation = "true" };
            await PostAsync("/api/p/edit", payload);
        }

        // 🔧 COMMON POST HANDLER
        private async Task<string> PostAsync(string url, object payload)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Delhivery API error: {responseBody}");

            return responseBody;
        }
    }
}
