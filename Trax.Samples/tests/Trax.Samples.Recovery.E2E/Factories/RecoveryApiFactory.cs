using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Core.Decisions;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Factories;

/// <summary>
/// Boots the whole Recovery host (GraphQL, scheduler, local workers) against a dedicated test
/// database, with the demo model swapped for a <see cref="CountingDecider"/> and the demo's pauses
/// shortened. Defaults to the CI Postgres service (port 5432, database <c>recovery_e2e_tests</c>);
/// <c>TRAX_TEST_PG_PORT</c> moves the port.
/// </summary>
public sealed class RecoveryApiFactory : SampleApiFactory<Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=recovery_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    public CountingDecider Decider { get; } = new();

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");

        // Long enough that a test subscribes to an attempt before its first question is asked.
        builder.UseSetting("Recovery:StepDelay", "00:00:00.400");
        builder.UseSetting("Recovery:ModelLatencyMin", "00:00:00");
        builder.UseSetting("Recovery:ModelLatencyMax", "00:00:00");

        // Sweep the scheduler's own runs within seconds, so a test can watch it happen.
        builder.UseSetting("Recovery:SchedulerRunRetention", "00:00:03");
        builder.UseSetting("Recovery:CleanupInterval", "00:00:01");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDecider>();
            services.AddSingleton<IDecider>(Decider);
        });
    }
}
