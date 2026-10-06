using Trax.Samples.GraphQLClient.BillingServer;

// Standalone billing server. Run with:
//   dotnet run --project Trax.Samples.GraphQLClient.BillingServer
// It listens on http://localhost:5314 in Development (Properties/launchSettings.json). The Gateway
// starts this server in-process on 5311 instead, so the two can run side by side.
// Serves GraphQL at /trax/graphql (discover.billing.getInvoice) and health at /trax/health.
BillingServerHost.Build(args).Run();

namespace Trax.Samples.GraphQLClient.BillingServer
{
    // Exposes the entry point to WebApplicationFactory<Program> in the E2E suite.
    public partial class Program;
}
