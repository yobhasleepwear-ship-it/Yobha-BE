namespace ShoppingPlatform.DTOs
{
    public class CreateAddressDto
    {
        public string? Id { get; set; } // optional, server will generate if missing
        public string FullName { get; set; } = null!;
        public string Line1 { get; set; } = null!;
        public string? Line2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Zip { get; set; } = null!;
        public string Country { get; set; } = null!;
        public bool IsDefault { get; set; } = false;
        public string MobileNumnber { get; set; } = null!;
    }

    public class UpdateAddressDto
    {
        public string FullName { get; set; } = null!;
        public string Line1 { get; set; } = null!;
        public string? Line2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Zip { get; set; } = null!;
        public string Country { get; set; } = null!;
        public bool IsDefault { get; set; } = false;
        public string MobileNumnber { get; set; } = null!;

    }

    public class UpdateNameDto
    {
        public string FullName { get; set; } = null!;
    }
}
