using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.GraphQLClient.E2E.Factories;

/// <summary>
/// Boots the inventory server (server B) in-process. WebApplicationFactory runs the host in
/// Development, where Trax serves introspection, so the outbound client can fetch the schema.
/// </summary>
public class InventoryServerFactory : WebApplicationFactory<InventoryServer.Program>;
