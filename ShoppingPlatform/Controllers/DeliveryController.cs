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

        public DeliveryController(IDeliveryService deliveryService, IOrderRepository orderRepository, IBuybackService buybackRepo, IReturnRepository returnRepo, ISecretsRepository secretsRepo)
        {
            _deliveryService = deliveryService;
            _orderRepo = orderRepository;
            _buybackRepo = buybackRepo;
            _returnRepo = returnRepo;
            _secretsRepo = secretsRepo;
        }

        [HttpPost("create-shipment")]
        public async Task<IActionResult> CreateShipment(
    [FromBody] DeliveryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.OrderId))
                return BadRequest("OrderId is required");

            string awb;
            var secretsDoc = await _secretsRepo.GetSecretsByAddedForAsync("");

            // 🌍 INTERNATIONAL
            if (request.IsInternational)
            {
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
            // 🇮🇳 DOMESTIC (FORWARD + REVERSE)
            else
            {
                bool isReverse =
                    request.ReferenceType == "Return" ||
                    request.ReferenceType == "Buyback";

                var domesticRequest = new DomesticShipmentRequest
                {
                    OrderId = request.OrderId,
                    IsReverse = isReverse,

                    // Pickup
                    PickupName = request.PickupName,
                    PickupPhone = request.PickupPhone,
                    PickupAddress = request.PickupAddress,
                    PickupPincode = request.PickupPincode,

                    // Drop
                    DropName = request.DropName,
                    DropPhone = request.DropPhone,
                    DropAddress = request.DropAddress,
                    DropPincode = request.DropPincode,

                    Weight = request.Weight,
                    IsCod = request.IsCod,
                    CodAmount = request.IsCod ? request.CodAmount : 0
                };

                awb = await _deliveryService.CreateDomesticShipmentAsync(domesticRequest);
            }

            // ✅ Build DeliveryDetails (THIS is what you wanted)
            var deliveryDetails = new DeliveryDetails
            {
                Awb = awb,
                Courier = "DELHIVERY",

                Status = "READY_TO_SHIP",

                Type = request.ReferenceType ,

                IsCod = request.IsCod,
                CodAmount = request.IsCod ? request.CodAmount : 0,

                IsInternational = request.IsInternational,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // ✅ Pass DeliveryDetails (NOT DeliveryRequest)
            var success = await UpdateShipmentAsync(
                request.OrderId,
                request.ReferenceType,
                deliveryDetails);

            return Ok(deliveryDetails);
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



    }

}
