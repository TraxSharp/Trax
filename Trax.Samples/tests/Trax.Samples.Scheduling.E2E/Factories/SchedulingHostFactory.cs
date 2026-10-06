using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E.Factories;

/// <summary>
/// Starts the Scheduling sample's host, in Development (the <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// default), against its own test database. Everything else, the demo-speed polling and retry
/// settings included, is the sample's own <c>Program.cs</c>.
/// </summary>
public class SchedulingHostFactory : SampleApiFactory<Host.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=scheduling_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    public static string TestConnectionString { get; } =
        TestPostgres.WithPort(DefaultConnectionString);

    protected override string ConnectionString => TestConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        // The sample starts with a three-call supplier outage to show a dead letter on its own.
        // Here the host starts healthy: each test starts its own outage, and a startup outage
        // still under way would leave failures that count toward the test's dead letter.
        builder.UseSetting("SupplierFeed:OutageCalls", "0");
    }
}
