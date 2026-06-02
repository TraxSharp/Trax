using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.GraphQLClient.E2E.Factories;

/// <summary>
/// Boots the inventory server (server B) in-process. Introspection is enabled in the host
/// itself, so no environment override is needed for the outbound client to fetch the schema.
/// </summary>
public class InventoryServerFactory : WebApplicationFactory<InventoryServer.Program>;
