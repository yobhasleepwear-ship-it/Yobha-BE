using ShoppingPlatform.DTOs;

namespace ShoppingPlatform.Services
{
    public interface IDeliveryService
    {
        Task<string> CreateInternationalShipmentAsync(InternationalDeliveryRequest request);
        Task<string> TrackShipmentAsync(string awb);
        Task CancelShipmentAsync(string awb);
        Task<string> CreateDomesticShipmentAsync(DomesticShipmentRequest request);
        Task SchedulePickupAsync(string awb);
        Task<DelhiveryPincodeResponse> CheckPincodeServiceabilityAsync(string pincode);

    }

}
