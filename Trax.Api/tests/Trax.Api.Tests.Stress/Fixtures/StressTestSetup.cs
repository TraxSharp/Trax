using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Api.Extensions;
using Trax.Api.Tests.Stress.Fakes.Trains;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Api.Tests.Stress.Fixtures;

/// <summary>
/// Base fixture for admin-endpoint stress tests. Builds the real Trax DI container
/// (effects + mediator + scheduler + api services) against a dedicated Postgres database,
/// seeds it once to millions of rows, and exposes a warm-up-then-measure helper that
/// asserts each endpoint stays within a dashboard-acceptable latency budget.
/// </summary>
/// <remarks>
/// Ignored by default so the suite never runs in normal CI (seeding millions of rows takes
/// minutes). Run explicitly:
/// <code>dotnet test --filter TestCategory=Stress</code>
/// Row counts and the target database come from <c>appsettings.json</c> and the
/// <c>TRAX_STRESS_*</c> environment variables (see <see cref="StressProfile"/>). Because
/// hosted services only start under a running host, building a plain <see cref="ServiceProvider"/>
/// here means the scheduler's background pollers never run and never contend with the reads
/// under measurement.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Ignore("Stress tests — run manually with: dotnet test --filter TestCategory=Stress")]
public abstract class StressTestSetup
{
    private ServiceProvider _serviceProvider = null!;

    protected static readonly StressProfile Profile = StressProfile.FromEnvironment();

    /// <summary>Budget for a single paginated list read (keyset, first page, or indexed filter).</summary>
    protected static readonly TimeSpan ListBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Budget for the dashboard metrics block (several aggregations over the 7-day window).
    /// Set below the pre-index cost (~630-770ms at 3M rows) so dropping the metrics covering
    /// indexes from migration 037 fails this suite; the covering indexes land it at ~350-470ms.
    /// </summary>
    protected static readonly TimeSpan MetricsBudget = TimeSpan.FromMilliseconds(600);

    /// <summary>Budget for the health snapshot (polled continuously by the dashboard).</summary>
    protected static readonly TimeSpan HealthBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Budget for the cluster (hosts) rollup: a full, time-unfiltered aggregation of the metadata
    /// table by host instance. That is inherently O(rows) and can't be made grid-fast at millions
    /// of rows, but it is a refresh-on-demand admin view (the operator opens the Cluster page), not
    /// a hot poll or a paginated scroll, so a ~1s SLA is appropriate. The ix_metadata_host_rollup
    /// covering index (migration 039) keeps it near that floor with a heap-free index-only scan.
    /// </summary>
    protected static readonly TimeSpan ClusterBudget = TimeSpan.FromMilliseconds(1200);

    /// <summary>Budget for in-memory / config reads that never touch a large table.</summary>
    protected static readonly TimeSpan TrivialBudget = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Connection to the dedicated stress database. Override with <c>TRAX_STRESS_CONNECTION</c>;
    /// defaults to <c>trax_api_stress</c> on the local cluster. <c>Command Timeout</c> is large
    /// because seeding runs multi-million-row inserts on this connection.
    /// </summary>
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("TRAX_STRESS_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=trax_api_stress;Username=trax;Password=trax123;"
            + "Maximum Pool Size=16;Timeout=30;Command Timeout=1200;Include Error Detail=true";

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var connectionString = ConnectionString;
        BulkSeeder.EnsureDatabaseExists(connectionString);

        _serviceProvider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Warning)
                            .SaveTrainParameters()
                            .UsePostgres(connectionString)
                            .AddJson()
                    )
                    .AddMediator(typeof(StressProbeTrain).Assembly, typeof(JobRunnerTrain).Assembly)
                    // Default worker mode is fine: the suite only reads. Hosted worker
                    // services never start because this is a plain ServiceProvider, not a host.
                    .AddScheduler(scheduler => scheduler)
            )
            .AddTraxApi()
            .BuildServiceProvider();

        await BulkSeeder.SeedAsync(
            connectionString,
            Profile,
            message => TestContext.Progress.WriteLine($"[seed] {message}")
        );
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _serviceProvider.DisposeAsync();

    /// <summary>
    /// Runs <paramref name="action"/> once to warm the connection pool / query plan, then a
    /// second time under a stopwatch. Asserts the steady-state elapsed time is within
    /// <paramref name="budget"/> and returns it. Steady state is what the dashboard experiences
    /// once a page has loaded once, which is the number that matters for "does it slow down".
    /// </summary>
    protected async Task<TimeSpan> MeasureAsync(
        string label,
        TimeSpan budget,
        Func<IServiceProvider, CancellationToken, Task> action
    )
    {
        // Warm-up (untimed).
        using (var warm = _serviceProvider.CreateScope())
            await action(warm.ServiceProvider, CancellationToken.None);

        using var scope = _serviceProvider.CreateScope();
        var sw = Stopwatch.StartNew();
        await action(scope.ServiceProvider, CancellationToken.None);
        sw.Stop();

        TestContext.Out.WriteLine(
            $"{label}: {sw.Elapsed.TotalMilliseconds:F0}ms "
                + $"(budget {budget.TotalMilliseconds:F0}ms, {Profile.Metadata:N0} metadata rows)"
        );

        sw.Elapsed.Should()
            .BeLessThan(
                budget,
                $"{label} must stay within {budget.TotalMilliseconds:F0}ms at "
                    + $"{Profile.Metadata:N0} metadata / {Profile.Log:N0} log rows"
            );

        return sw.Elapsed;
    }

    /// <summary>Times <paramref name="action"/> once (warm-up + measure) without asserting a budget.</summary>
    protected async Task<TimeSpan> TimeAsync(Func<IServiceProvider, CancellationToken, Task> action)
    {
        using (var warm = _serviceProvider.CreateScope())
            await action(warm.ServiceProvider, CancellationToken.None);

        using var scope = _serviceProvider.CreateScope();
        var sw = Stopwatch.StartNew();
        await action(scope.ServiceProvider, CancellationToken.None);
        sw.Stop();
        return sw.Elapsed;
    }
}
