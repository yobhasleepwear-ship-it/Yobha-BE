using System.ComponentModel.DataAnnotations;

namespace ShoppingPlatform.DTOs
{
    public class CreateAddressDto
    {
        public string? Id { get; set; } // optional, server will generate if missing
        [Required]
        public string FullName { get; set; } = null!;
        [Required]
        public string Line1 { get; set; } = null!;
        public string? Line2 { get; set; }
        [Required]
        public string City { get; set; } = null!;
        [Required]
        public string State { get; set; } = null!;
        [Required]
        public string Zip { get; set; } = null!;
        [Required]
        public string Country { get; set; } = null!;
        public bool IsDefault { get; set; } = false;
        [Required]
        public string MobileNumnber { get; set; } = null!;
        [Required]
        public string countryCode { get; set; } = null!;

    }

    public class UpdateAddressDto
    {
        [Required]
        public string FullName { get; set; } = null!;
        [Required]
        public string Line1 { get; set; } = null!;
        public string? Line2 { get; set; }
        [Required]
        public string City { get; set; } = null!;
        [Required]
        public string State { get; set; } = null!;
        [Required]
        public string Zip { get; set; } = null!;
        [Required]
        public string Country { get; set; } = null!;
        public bool IsDefault { get; set; } = false;
        [Required]
        public string MobileNumnber { get; set; } = null!;
        [Required]
        public string countryCode { get; set; } = null!;
        
    }

    public class UpdateNameDto
    {
        public string FullName { get; set; } = null!;
    }
}
