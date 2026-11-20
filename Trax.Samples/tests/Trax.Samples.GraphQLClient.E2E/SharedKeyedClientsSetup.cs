using Trax.Core.Extensions;
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
    private static InventoryServerFactory? _inventory;
    private static BillingServerFactory? _billing;

    public static ServiceProvider Clients
    {
        get => field.NotNull(nameof(Clients));
        private set;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _inventory = new InventoryServerFactory();
        _billing = new BillingServerFactory();

        var services = new ServiceCollection();
        services
            .AddKeyedTraxGraphQLClient("serverB", new Uri("http://localhost/trax/graphql"))
            .ConfigureHttpClient(_inventory.NotNull().CreateClient());
        services
            .AddKeyedTraxGraphQLClient("serverC", new Uri("http://localhost/trax/graphql"))
            .ConfigureHttpClient(_billing.NotNull().CreateClient());

        Clients = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Clients.DisposeAsync();
        if (_inventory is not null)
            await _inventory.DisposeAsync().ConfigureAwait(false);
        if (_billing is not null)
            await _billing.DisposeAsync().ConfigureAwait(false);
    }
}
