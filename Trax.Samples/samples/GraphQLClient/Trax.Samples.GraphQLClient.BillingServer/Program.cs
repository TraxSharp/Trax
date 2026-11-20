using Trax.Samples.GraphQLClient.BillingServer;

// Standalone billing server. Run with:
//   dotnet run --project Trax.Samples.GraphQLClient.BillingServer
// It listens on http://localhost:5314 in Development (Properties/launchSettings.json). The Gateway
// starts this server in-process on 5311 instead, so the two can run side by side.
// Serves GraphQL at /trax/graphql (discover.billing.getInvoice) and health at /trax/health.
BillingServerHost.Build(args).Run();

namespace Trax.Samples.GraphQLClient.BillingServer
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
