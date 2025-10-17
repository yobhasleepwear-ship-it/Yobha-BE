using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using ShoppingPlatform.Models;

namespace ShoppingPlatform.DTOs
{
    public class AddOrUpdateCartRequest
    {
        [Required] public string ProductId { get; set; } = string.Empty; // readable PID (ex: PID10001)
        public string? VariantSku { get; set; } = string.Empty; // optional — variant sku if product has variants
        [Range(1, 9999)] public int Quantity { get; set; } = 1;

        // Optional: client can pass the currency preference (server will validate)
        public string? Currency { get; set; } = "INR";

        // Optional: if client wants to add any note / metadata for this item
        public string? Note { get; set; }
    }

    public class UpdateCartQuantityRequest
    {
        [Required] public string CartItemId { get; set; } = string.Empty;
        [Range(1, 9999)] public int Quantity { get; set; }
    }

    public class CartProductSnapshot
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

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal UnitPrice { get; set; } = 0m;

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal? CompareAtPrice { get; set; }

        public string Currency { get; set; } = "INR";
        public int StockQuantity { get; set; } = 0;
        public int ReservedQuantity { get; set; } = 0;
        public bool IsActive { get; set; } = true;
        public bool FreeShipping { get; set; } = false;
        public bool CashOnDelivery { get; set; } = false;

        public List<ShoppingPlatform.DTOs.PriceTier>? PriceList { get; set; }
        public CountryPrice? countryPrice { get; set; }
    }   

    public class PriceTier
    {
        public string Id { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public decimal PriceAmount { get; set; }
        public int Quantity { get; set; }
        public string Currency { get; set; } = "INR";
    }

    public class CartItemResponse
    {
        public string Id { get; set; } = string.Empty; // cart item id (mongo)
        public string UserId { get; set; } = string.Empty;
        public CartProductSnapshot Product { get; set; } = new CartProductSnapshot();
        public int Quantity { get; set; } = 1;

        // Calculated fields
        public decimal LineTotal => Decimal.Round(Product.UnitPrice * Quantity, 2);

        public DateTime AddedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Note { get; set; }

        // NEW: status and messages for the API consumer
        public bool Success { get; set; } = true;
        public string? Message { get; set; }

        // NEW: when currency mismatch occurs, return the matching CountryPrice suggestion (if any)
        public ShoppingPlatform.Models.CountryPrice? SuggestedCountryPrice { get; set; }
    }

    public class CartSummary
    {
        public int TotalItems { get; set; }
        public int DistinctItems { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Shipping { get; set; }
        public decimal Tax { get; set; }
        public decimal Discount { get; set; }
        public decimal GrandTotal { get; set; }
        public string Currency { get; set; } = "INR";
    }

    public class CartResponse
    {
        public IEnumerable<CartItemResponse> Items { get; set; } = Array.Empty<CartItemResponse>();
        public CartSummary Summary { get; set; } = new CartSummary();
    }
}
