using Exante.Net;
using QuantConnect.Configuration;

namespace QuantConnect.ExanteBrokerage.Tests
{
    public class ExanteBrokerageHelper
    {
        public static ExanteClientOptions ClientOptions()
        {
            var clientId = Config.Get("exante-client-id");
            var applicationId = Config.Get("exante-application-id");
            var sharedKey = Config.Get("exante-shared-key");
            var platformTypeStr = Config.Get("exante-platform-type");
            var exanteClientOptions =
                ExanteBrokerageFactory.CreateExanteClientOptions(clientId, applicationId, sharedKey, platformTypeStr);
            
            return exanteClientOptions;
        }
    }
}