using Exante.Net;
using Exante.Net.Enums;
using NUnit.Framework;
using QuantConnect.Configuration;

namespace QuantConnect.ExanteBrokerage.Tests;

public class ExanteClientTest
{
    private static ExanteClient CreateExanteClient()
    {
        var clientId = Config.Get("exante-client-id");
        var applicationId = Config.Get("exante-application-id");
        var sharedKey = Config.Get("exante-shared-key");
        const ExantePlatformType platformType = ExantePlatformType.Demo;

        var exanteClientOptions =
            new ExanteClientOptions(
                new ExanteApiCredentials(
                    clientId,
                    applicationId,
                    sharedKey
                ),
                platformType
            );
        var exanteClient = new ExanteClient(exanteClientOptions);
        return exanteClient;
    }

    [Test]
    public void AccountSummaryWorks()
    {
        var exanteClient = CreateExanteClient();
        var accountId = Config.Get("exante-account-id");
        var accountSummary = exanteClient.GetAccountSummaryAsync(accountId, "USD").Result;

        Assert.IsTrue(accountSummary.Success);
    }
}