using Trax.Samples.GraphQLClient.E2E.Factories;

namespace Trax.Samples.GraphQLClient.E2E;

/// <summary>
/// Boots both servers once for the whole suite and builds a single container holding two
/// keyed clients ("serverB" -> inventory, "serverC" -> billing), each wired to its server's
/// in-process HttpClient. This is the real proof: two differently-schema'd servers, two keyed
/// clients, one ServiceCollection.
/// </summary>
[SetUpFixture]
public class SharedKeyedClientsSetup
{
    private static InventoryServerFactory _inventory = null!;
    private static BillingServerFactory _billing = null!;

    public static ServiceProvider Clients { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _inventory = new InventoryServerFactory();
        _billing = new BillingServerFactory();

        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("serverB", new Uri("http://localhost/trax/graphql"))
            .ConfigureHttpClient(_inventory.CreateClient());
        services
            .AddKeyedTraxGraphQLClient("serverC", new Uri("http://localhost/trax/graphql"))
            .ConfigureHttpClient(_billing.CreateClient());

        Clients = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Clients.DisposeAsync();
        await _inventory.DisposeAsync();
        await _billing.DisposeAsync();
    }
}
