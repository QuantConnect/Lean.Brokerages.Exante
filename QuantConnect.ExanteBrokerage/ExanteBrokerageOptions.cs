using System;
using Exante.Net;
using Exante.Net.Enums;
using QuantConnect.Configuration;

namespace QuantConnect.ExanteBrokerage
{
    public class ExanteBrokerageOptions
    {
        public string AccountId { get; }
        private readonly string _clientId;
        private readonly string _applicationId;
        private readonly string _sharedKey;
        private readonly string _platformType;

        public ExanteBrokerageOptions(
            string accountId,
            string clientId,
            string applicationId,
            string sharedKey,
            string platformType
        )
        {
            AccountId = accountId;
            _clientId = clientId;
            _applicationId = applicationId;
            _sharedKey = sharedKey;
            _platformType = platformType;
        }


        public ExanteClientOptions ExanteClientOptions()
        {
            var platformTypeParsed = Enum.TryParse(_platformType, true, out ExantePlatformType platformType);
            if (!platformTypeParsed)
            {
                throw new Exception($"ExantePlatformType parse error: {_platformType}");
            }

            var exanteClientOptions =
                new ExanteClientOptions(
                    new ExanteApiCredentials(_clientId, _applicationId, _sharedKey),
                    platformType
                );
            return exanteClientOptions;
        }

        public static ExanteBrokerageOptions FromConfig()
        {
            var clientId = Config.Get("exante-client-id");
            var applicationId = Config.Get("exante-application-id");
            var sharedKey = Config.Get("exante-shared-key");
            var platformType = Config.Get("exante-platform-type");
            var accountId = Config.Get("exante-account-id");
            var options = new ExanteBrokerageOptions(accountId, clientId, applicationId, sharedKey, platformType);
            return options;
        }
    }
}