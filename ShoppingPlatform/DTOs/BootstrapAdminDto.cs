namespace ShoppingPlatform.DTOs
{
    public class BootstrapAdminDto
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string Secret { get; set; } = null!;
        public string? FullName { get; set; }
    }
}
