using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.PersistedOperations.E2E.Fixtures;

/// <summary>
/// WebApplicationFactory hosting <c>Trax.Samples.PersistedOperations.Api</c>
/// in-process. Tests POST against the GraphQL endpoint via the factory's
/// <c>HttpClient</c> (no Kestrel binding, no port collisions).
/// </summary>
/// <remarks>
/// The host runs against a dedicated database, <c>persisted_operations_e2e_tests</c>, on the CI
/// Postgres service port, so a test run never touches the <c>trax</c> database a developer runs the
/// sample against. <c>TRAX_TEST_PG_PORT</c> moves the port for a machine where 5432 is taken.
/// </remarks>
public sealed class PersistedOperationsApiFactory : WebApplicationFactory<Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=persisted_operations_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=8;Minimum Pool Size=0";

    /// <summary>
    /// Override knobs for individual test classes (e.g., start the host in
    /// Production). Default is null (use whatever the sample's Program.cs configures).
    /// </summary>
    public Action<IWebHostBuilder>? Configure { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:TraxDatabase",
            TestPostgres.WithPort(DefaultConnectionString)
        );
        Configure?.Invoke(builder);
    }
}
