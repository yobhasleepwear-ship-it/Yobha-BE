namespace ShoppingPlatform.DTOs
{
    public class DeliveryRequest
    {
        public string OrderId { get; set; }
        public string ReferenceType { get; set; } // Order | Buyback | Return 

        // Pickup (source)
        public string PickupName { get; set; }
        public string PickupPhone { get; set; }
        public string PickupAddress { get; set; }
        public string PickupPincode { get; set; }

        // Drop (destination)
        public string DropName { get; set; }
        public string DropPhone { get; set; }
        public string DropAddress { get; set; }
        public string DropPincode { get; set; }

        public decimal Weight { get; set; }

        public bool IsCod { get; set; }
        public decimal CodAmount { get; set; }

        // International
        public bool IsInternational { get; set; }
        public string? CountryCode { get; set; }
        public string? Commodity { get; set; }
        public decimal DeclaredValue { get; set; }
        public string? Currency { get; set; }
    }


    public class InternationalDeliveryRequest
    {
        public string OrderId { get; set; }
        public string CountryCode { get; set; }
        public string Commodity { get; set; }
        public decimal Weight { get; set; }
        public decimal Value { get; set; }
        public string Currency { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
    }

    public class ShipmentResult
    {
        public string Awb { get; set; }
        public string Courier { get; set; } = "DELHIVERY";
        public string Status { get; set; }
    }

    public class DomesticShipmentRequest
    {
        public string OrderId { get; set; }
        public bool IsReverse { get; set; }

        // Pickup
        public string PickupName { get; set; }
        public string PickupPhone { get; set; }
        public string PickupAddress { get; set; }
        public string PickupPincode { get; set; }

        // Drop
        public string DropName { get; set; }
        public string DropPhone { get; set; }
        public string DropAddress { get; set; }
        public string DropPincode { get; set; }

        public decimal Weight { get; set; }

        public bool IsCod { get; set; }
        public decimal CodAmount { get; set; }
    }

    public class DeliveryStatusUpdateRequest
    {
        public string Awb { get; set; }
        public string Status { get; set; }          // e.g. "In Transit"
        public string? StatusCode { get; set; }     // Optional
        public DateTime? StatusDateTime { get; set; }
        public string? Location { get; set; }
    }
    public class ShipmentReference
    {
        public string ReferenceId { get; set; }
        public string ReferenceType { get; set; } // Order / Buyback / Return
    }

}
