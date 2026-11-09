using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.CheckIn.E2E.Fixtures;

/// <summary>
/// Boots the CheckIn host against a dedicated test database (<c>checkin_e2e_tests</c>, which CI provisions), with
/// persisted operations required as it ships, and sweeps for due timers every 200 ms, so the desk's and the flight's
/// timers are seen when they fall due.
/// </summary>
public class CheckInApiFactory : SampleApiFactory<Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=checkin_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("CheckIn:MachineSweepInterval", "00:00:00.200");
    }
}
