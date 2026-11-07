using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Bakery.E2E.Fixtures;

/// <summary>
/// Boots the Bakery host against a dedicated test database (<c>bakery_e2e_tests</c>, which CI provisions). It accepts
/// hand-written documents, since the feature bindings send each step as one, and sweeps for due timers every 200 ms, so
/// the shop's closing time is seen when it falls due.
/// </summary>
public class BakeryApiFactory : SampleApiFactory<Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=bakery_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("Bakery:RequirePersistedOperations", "false");
        builder.UseSetting("Bakery:MachineSweepInterval", "00:00:00.200");
    }
}
