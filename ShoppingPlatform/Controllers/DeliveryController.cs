using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShoppingPlatform.DTOs;
using ShoppingPlatform.Models;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;

namespace ShoppingPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeliveryController : ControllerBase
    {
        private readonly IDeliveryService _deliveryService;
        private readonly IOrderRepository _orderRepo;
        private readonly IBuybackService _buybackRepo;
        private readonly IReturnRepository _returnRepo;
        private readonly ISecretsRepository _secretsRepo;
        private readonly ILogger<DeliveryController> _logger;

        public DeliveryController(IDeliveryService deliveryService, IOrderRepository orderRepository, IBuybackService buybackRepo, IReturnRepository returnRepo, ISecretsRepository secretsRepo, ILogger<DeliveryController> logger)
        {
            _deliveryService = deliveryService;
            _orderRepo = orderRepository;
            _buybackRepo = buybackRepo;
            _returnRepo = returnRepo;
            _secretsRepo = secretsRepo;
            _logger = logger;
        }

        [HttpPost("create-shipment")]
        public async Task<IActionResult> CreateShipment([FromBody] DeliveryRequest request)
        {
            _logger.LogInformation("CreateShipment called with payload: {@Request}", request);

            if (string.IsNullOrWhiteSpace(request.OrderId))
            {
                _logger.LogWarning("OrderId missing in CreateShipment request");
                return BadRequest("OrderId is required");
            }

            try
            {
                _logger.LogInformation(
                    "Shipment type decision | IsInternational: {IsInternational}, ReferenceType: {ReferenceType}",
                    request.IsInternational,
                    request.ReferenceType
                );

                // 🔐 Fetch secrets
                var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync("DELHIVERY");

                if (secretsDoc == null)
                {
                    _logger.LogError("Delhivery secrets not found in DB");
                    return StatusCode(500, "Delhivery secrets not configured");
                }

                string awb;

                // 🌍 INTERNATIONAL
                if (request.IsInternational)
                {
                    _logger.LogInformation("Creating INTERNATIONAL shipment for OrderId {OrderId}", request.OrderId);

                    awb = await _deliveryService.CreateInternationalShipmentAsync(
                        new InternationalDeliveryRequest
                        {
                            OrderId = request.OrderId,
                            CountryCode = request.CountryCode,
                            Name = request.DropName,
                            Address = request.DropAddress,
                            Weight = request.Weight,
                            Value = request.DeclaredValue,
                            Currency = request.Currency,
                            Commodity = request.Commodity
                        });
                }
                else
                {
                    bool isReverse =
                        request.ReferenceType == "Return" ||
                        request.ReferenceType == "Buyback";

                    _logger.LogInformation(
                        "Creating DOMESTIC shipment | IsReverse: {IsReverse}",
                        isReverse
                    );

                    var domesticRequest = new DomesticShipmentRequest
                    {
                        OrderId = request.OrderId,
                        IsReverse = isReverse,

                        PickupName = request.PickupName,
                        PickupPhone = request.PickupPhone,
                        PickupAddress = request.PickupAddress,
                        PickupPincode = request.PickupPincode,

                        DropName = request.DropName,
                        DropPhone = request.DropPhone,
                        DropAddress = request.DropAddress,
                        DropPincode = request.DropPincode,

                        Weight = request.Weight,
                        IsCod = request.IsCod,
                        CodAmount = request.IsCod ? request.CodAmount : 0
                    };

                    _logger.LogDebug("Domestic shipment payload: {@DomesticRequest}", domesticRequest);

                    awb = await _deliveryService.CreateDomesticShipmentAsync(domesticRequest);
                }

                _logger.LogInformation("Shipment created successfully. AWB: {Awb}", awb);

                var deliveryDetails = new DeliveryDetails
                {
                    Awb = awb,
                    Courier = "DELHIVERY",
                    Status = "READY_TO_SHIP",
                    Type = request.IsInternational
                        ? "International"
                        : (request.ReferenceType == "Order" ? "Forward" : "Reverse"),
                    IsCod = request.IsCod,
                    CodAmount = request.IsCod ? request.CodAmount : 0,
                    IsInternational = request.IsInternational,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _logger.LogInformation("Saving delivery details for OrderId {OrderId}", request.OrderId);

                await UpdateShipmentAsync(
                    request.OrderId,
                    request.ReferenceType,
                    deliveryDetails);

                _logger.LogInformation("Delivery details saved successfully for AWB {Awb}", awb);

                return Ok(deliveryDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "CreateShipment failed for OrderId {OrderId}. Error: {Message}",
                    request.OrderId,
                    ex.Message
                );

                return StatusCode(500, new
                {
                    message = "Create shipment failed",
                    error = ex.Message
                });
            }
        }


        // 🔹 TRACK SHIPMENT
        [HttpGet("track/{awb}")]
        public async Task<IActionResult> TrackShipment(string awb)
        {
            var result = await _deliveryService.TrackShipmentAsync(awb);
            return Ok(result);
        }

        // 🔹 CANCEL SHIPMENT
        [HttpPost("cancel/{awb}")]
        public async Task<IActionResult> CancelShipment(string awb)
        {
            await _deliveryService.CancelShipmentAsync(awb);
            return Ok();
        }

        [HttpPost("schedule-pickup/{awb}")]
        public async Task<IActionResult> SchedulePickup(
            string awb,
            string referenceId,
            string referenceType)
        {
            await _deliveryService.SchedulePickupAsync(awb);

            await UpdateDeliveryStatusAsync(
                referenceId,
                referenceType,
                "PICKUP_SCHEDULED");

            return Ok(new
            {
                awb,
                status = "PICKUP_SCHEDULED"
            });
        }

        [HttpPost("status-update")]
        public async Task<IActionResult> UpdateDeliveryStatusFromCourier(
    [FromBody] DeliveryStatusUpdateRequest request)
        {
            if (!Request.Headers.TryGetValue("X-DELHIVERY-TOKEN", out var token))
                return Unauthorized("Missing token");

            var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync("DELHIVERY");

            if (secretsDoc == null || token != secretsDoc.DelhiveryWebhookToken)
                return Unauthorized("Invalid token");

            if (string.IsNullOrWhiteSpace(request.Awb))
                return BadRequest("AWB is required");

            if (string.IsNullOrWhiteSpace(request.Status))
                return BadRequest("Status is required");

            // 1️⃣ Find shipment by AWB
            var shipment = await FindShipmentByAwbAsync(request.Awb);

            if (shipment == null)
                return NotFound("Shipment not found for given AWB");

            // 2️⃣ Map courier status → internal status
            var internalStatus = MapCourierStatus(request.Status);

            // 3️⃣ Update status
           var res = await UpdateDeliveryStatusAsync(
                shipment.ReferenceId,
                shipment.ReferenceType,
                internalStatus);

            return Ok(new
            {
                awb = request.Awb,
                status = internalStatus
            });
        }


        private async Task<bool> UpdateShipmentAsync(
     string referenceId,
     string referenceType,
     DeliveryDetails deliveryDetails)
        {
            switch (referenceType)
            {
                case "Order":
                    return await _orderRepo.UpdateDeliveryDetailsAsync(
                        referenceId,
                        deliveryDetails);
                case "Buyback":
                    return await _buybackRepo.UpdateDeliveryDetailsAsync(
                        referenceId,
                        deliveryDetails);

                case "Return":
                    return await _returnRepo.UpdateDeliveryDetailsAsync(
                        referenceId,
                        deliveryDetails);

                default:
                    throw new Exception("Invalid ReferenceType");
            }
        }

        private async Task<bool> UpdateDeliveryStatusAsync(
            string referenceId,
            string referenceType,
            string newStatus)
        {
            switch (referenceType)
            {
                case "Order":
                    return await _orderRepo.UpdateDeliveryStatusAsync(
                        referenceId,
                        newStatus);

                case "Buyback":
                    return await _buybackRepo.UpdateDeliveryStatusAsync(
                        referenceId,
                        newStatus);

                case "Return":
                    return await _returnRepo.UpdateDeliveryStatusAsync(
                        referenceId,
                        newStatus);

                default:
                    throw new Exception("Invalid ReferenceType");
            }
        }

        private async Task<ShipmentReference?> FindShipmentByAwbAsync(string awb)
        {
            var order = await _orderRepo.GetByAwbAsync(awb);
            if (order != null)
            {
                return new ShipmentReference
                {
                    ReferenceId = order.Id,
                    ReferenceType = "Order"
                };
            }

            var buyback = await _buybackRepo.GetByAwbAsync(awb);
            if (buyback != null)
            {
                return new ShipmentReference
                {
                    ReferenceId = buyback.Id,
                    ReferenceType = "Buyback"
                };
            }

            var ret = await _returnRepo.GetByAwbAsync(awb);
            if (ret != null)
            {
                return new ShipmentReference
                {
                    ReferenceId = ret.Id,
                    ReferenceType = "Return"
                };
            }

            return null;
        }

        private string MapCourierStatus(string courierStatus)
        {
            return courierStatus.ToLower() switch
            {
                "pickup scheduled" => "PICKUP_SCHEDULED",
                "picked up" => "PICKED_UP",
                "in transit" => "IN_TRANSIT",
                "out for delivery" => "OUT_FOR_DELIVERY",
                "delivered" => "DELIVERED",
                "rto" => "RTO",
                "cancelled" => "CANCELLED",
                "failed" => "FAILED",
                _ => "IN_TRANSIT" // safe default
            };
        }

    }

}
