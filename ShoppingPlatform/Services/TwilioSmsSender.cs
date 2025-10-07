using Microsoft.Extensions.Options;
using ShoppingPlatform.Configurations;
using System.Threading.Tasks;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ShoppingPlatform.Services
{
    public class TwilioSmsSender : ISmsSender
    {
        private readonly TwilioSettings _settings;

        public TwilioSmsSender(IOptions<TwilioSettings> opts)
        {
            _settings = opts.Value;
            if (!string.IsNullOrEmpty(_settings.AccountSid) && !string.IsNullOrEmpty(_settings.AuthToken))
            {
                TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
            }
        }

        public async Task SendSmsAsync(string toPhoneNumber, string message)
        {
            if (string.IsNullOrEmpty(_settings.AccountSid) || string.IsNullOrEmpty(_settings.AuthToken))
                throw new System.InvalidOperationException("Twilio not configured");

            var from = _settings.FromNumber;
            var msg = await MessageResource.CreateAsync(
                body: message,
                from: new PhoneNumber(from),
                to: new PhoneNumber(toPhoneNumber)
            );
        }
    }
}
