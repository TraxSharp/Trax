using Trax.Samples.GraphQLClient.InventoryServer;

// Standalone inventory server. Run with:
//   dotnet run --project Trax.Samples.GraphQLClient.InventoryServer
// It listens on http://localhost:5313 in Development (Properties/launchSettings.json). The Gateway
// starts this server in-process on 5310 instead, so the two can run side by side.
// Serves GraphQL at /trax/graphql (discover.inventory.getProduct) and health at /trax/health.
InventoryServerHost.Build(args).Run();

namespace Trax.Samples.GraphQLClient.InventoryServer
{
    // Exposes the entry point to WebApplicationFactory<Program> in the E2E suite.
    public partial class Program;
}
