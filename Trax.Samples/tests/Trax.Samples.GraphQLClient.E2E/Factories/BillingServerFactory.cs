using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.GraphQLClient.E2E.Factories;

/// <summary>
/// Boots the billing server (server C) in-process. Different schema from the inventory server,
/// so a client keyed to one cannot run the other's queries.
/// </summary>
public class BillingServerFactory : WebApplicationFactory<BillingServer.Program>;
