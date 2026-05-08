using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.PersistedOperations.E2E.Fixtures;

/// <summary>
/// WebApplicationFactory hosting <c>Trax.Samples.PersistedOperations.Api</c>
/// in-process. Tests POST against the GraphQL endpoint via the factory's
/// <c>HttpClient</c> (no Kestrel binding, no port collisions).
/// </summary>
public sealed class PersistedOperationsApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Override knobs for individual test classes (e.g., flip
    /// <c>RequirePersisted</c> off for shadow-mode tests). Default is null
    /// (use whatever the sample's Program.cs configures).
    /// </summary>
    public Action<IWebHostBuilder>? Configure { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The sample reads connection strings from configuration; in-process
        // tests assume the same docker-compose Postgres the sample uses.
        Configure?.Invoke(builder);
    }
}
