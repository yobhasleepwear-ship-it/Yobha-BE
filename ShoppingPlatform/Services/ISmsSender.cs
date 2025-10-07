namespace ShoppingPlatform.Services
{
    public interface ISmsSender
    {
        Task SendSmsAsync(string toPhoneNumber, string message);
    }

}
