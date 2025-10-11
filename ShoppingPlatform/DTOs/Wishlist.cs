using System.ComponentModel.DataAnnotations;

namespace ShoppingPlatform.DTOs
{
    public class AddWishlistRequest
    {
        [Required] public string ProductId { get; set; } = string.Empty; // PID
        public string? VariantSku { get; set; }
        public int DesiredQuantity { get; set; } = 1;
        public string? DesiredSize { get; set; }
        public string? DesiredColor { get; set; }
        public bool NotifyWhenBackInStock { get; set; } = true;
        public string? Note { get; set; }
    }

    public class WishlistProductDto
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? VariantSku { get; set; }
        public string? VariantId { get; set; }
        public string? VariantSize { get; set; }
        public string? VariantColor { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public string Currency { get; set; } = "INR";
        public bool IsActive { get; set; }
        public bool FreeShipping { get; set; }
    }

    public class WishlistItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public WishlistProductDto Product { get; set; } = new WishlistProductDto();
        public int DesiredQuantity { get; set; } = 1;
        public string? DesiredSize { get; set; }
        public string? DesiredColor { get; set; }
        public bool NotifyWhenBackInStock { get; set; }
        public bool MovedToCart { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
