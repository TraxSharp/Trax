using Trax.Samples.GraphQLClient.InventoryServer;

// Standalone inventory server. Run with:
//   dotnet run --project Trax.Samples.GraphQLClient.InventoryServer
// It listens on http://localhost:5313 in Development (Properties/launchSettings.json). The Gateway
// starts this server in-process on 5310 instead, so the two can run side by side.
// Serves GraphQL at /trax/graphql (discover.inventory.getProduct) and health at /trax/health.
await InventoryServerHost.Build(args).RunAsync().ConfigureAwait(false);

namespace Trax.Samples.GraphQLClient.InventoryServer
{
    /// <summary>
    /// Names this assembly for <c>WebApplicationFactory&lt;Program&gt;</c> in the E2E suite. It is
    /// never created: the host is the top-level program above.
    /// </summary>
    public sealed class Program
    {
        private Program() { }
    }
}
