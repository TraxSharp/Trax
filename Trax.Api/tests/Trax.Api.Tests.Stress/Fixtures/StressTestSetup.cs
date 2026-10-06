using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
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
/// Every concrete fixture carries <c>[Explicit]</c>, so the suite never runs in a normal
/// <c>dotnet test</c> (seeding millions of rows takes minutes) and runs when selected:
/// <code>dotnet test --filter TestCategory=Stress</code>
/// The attribute goes on the concrete fixture, not here: NUnit does not inherit
/// <c>[Explicit]</c> or <c>[Ignore]</c> from a base class, so one placed on this class skips
/// nothing.
/// Row counts and the target database come from <c>appsettings.json</c> and the
/// <c>TRAX_STRESS_*</c> environment variables (see <see cref="StressProfile"/>). Because
/// hosted services only start under a running host, building a plain <see cref="ServiceProvider"/>
/// here means the scheduler's background pollers never run and never contend with the reads
/// under measurement.
/// </remarks>
[TestFixture]
[Category("Stress")]
public abstract class StressTestSetup
{
    private ServiceProvider _serviceProvider = null!;

    protected static readonly StressProfile Profile = StressProfile.FromEnvironment();

    /// <summary>Budget for a single paginated list read (keyset, first page, or indexed filter).</summary>
    protected static readonly TimeSpan ListBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Budget for the dashboard metrics block (several aggregations over the 7-day window, read
    /// at once on separate connections, so it costs the slowest of them). Set below the cost
    /// without the metrics covering indexes from migration 037 (~500-760ms at 3M rows) so
    /// dropping them fails this suite; with them it lands at ~290-345ms.
    /// </summary>
    protected static readonly TimeSpan MetricsBudget = TimeSpan.FromMilliseconds(450);

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
    protected static string ConnectionString =>
        Environment.GetEnvironmentVariable("TRAX_STRESS_CONNECTION")
        ?? $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress;Username=trax;Password=trax123;"
            + "Maximum Pool Size=16;Timeout=30;Command Timeout=1200;Include Error Detail=true";

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var connectionString = ConnectionString;
        BulkSeeder.EnsureDatabaseExists(connectionString);

        var services = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            // An API host has ASP.NET authorization; the per-train check on enqueue needs it.
            .AddAuthorization()
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
            .AddTraxApi();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        await BulkSeeder.SeedAsync(
            connectionString,
            Profile,
            message => TestContext.Progress.WriteLine($"[seed] {message}")
        );
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _serviceProvider.DisposeAsync();

    /// <summary>
    /// Adds a fixture's own registrations to the container before it is built, for surfaces
    /// that <c>AddTraxApi()</c> does not register (persisted operations).
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>The fixture's container, for tests that drive a service directly.</summary>
    protected IServiceProvider Services => _serviceProvider;

    /// <summary>The role <see cref="AddOperationsGraphQL"/> gates the operations namespace to.</summary>
    protected const string AdminRole = "Admin";

    /// <summary>
    /// Adds the Trax schema with the operations namespace exposed and gated to
    /// <see cref="AdminRole"/>, as a production admin host has it, so a request runs the whole
    /// pipeline: validation, authorization, the error filter, serialization, and HotChocolate's
    /// execution timeout (30 s by default). Call it from <see cref="ConfigureServices"/>.
    /// </summary>
    protected static void AddOperationsGraphQL(IServiceCollection services) =>
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql =>
                graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .GateOperations(roles: AdminRole)
        );

    /// <summary>A signed-in caller holding <paramref name="role"/>.</summary>
    protected static ClaimsPrincipal Caller(string role) =>
        new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "stress"), new Claim(ClaimTypes.Role, role)],
                "Stress"
            )
        );

    private IRequestExecutor? _executor;

    /// <summary>
    /// Runs <paramref name="document"/> through the schema's request executor as
    /// <paramref name="caller"/> (an <see cref="AdminRole"/> holder by default) and returns the
    /// whole response. Needs <see cref="AddOperationsGraphQL"/>.
    /// </summary>
    protected async Task<JsonElement> ExecuteGraphQLAsync(
        string document,
        CancellationToken ct = default,
        ClaimsPrincipal? caller = null
    )
    {
        _executor ??= await _serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax", ct);

        var result = await _executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(document)
                .SetGlobalState(nameof(ClaimsPrincipal), caller ?? Caller(AdminRole))
                .Build(),
            ct
        );

        return JsonDocument.Parse(((OperationResult)result).ToJson()).RootElement.Clone();
    }

    /// <summary>
    /// <see cref="ExecuteGraphQLAsync"/>, failing the test on any GraphQL error, and returning the
    /// payload of the single field under <c>operations</c> (or under the namespace in
    /// <paramref name="path"/>, such as <c>deadLetters</c>).
    /// </summary>
    protected async Task<JsonElement> OperationsFieldAsync(
        string document,
        CancellationToken ct = default,
        params string[] path
    )
    {
        var response = await ExecuteGraphQLAsync(document, ct);
        response
            .TryGetProperty("errors", out _)
            .Should()
            .BeFalse($"the request must succeed: {response.GetRawText()}");

        var node = response.GetProperty("data").GetProperty("operations");
        foreach (var segment in path)
            node = node.GetProperty(segment);
        return node.EnumerateObject().Single().Value;
    }

    /// <summary>Runs one SQL statement against the stress database.</summary>
    protected static async Task ExecSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1200;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Reads one scalar from the stress database.</summary>
    protected static async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1200;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    /// <summary>
    /// The write counterpart of <see cref="MeasureAsync"/>. A write changes the rows it acts on,
    /// so a second run would measure a no-op (or a conflict). <paramref name="prepare"/> puts the
    /// rows into the state the write expects before the warm-up and before the timed run, and
    /// <paramref name="restore"/> (by default <paramref name="prepare"/> again) runs at the end,
    /// so every run does the same work and the suite leaves the database as the seed left it.
    /// </summary>
    protected async Task<TimeSpan> MeasureWriteAsync(
        string label,
        TimeSpan budget,
        Func<Task> prepare,
        Func<IServiceProvider, CancellationToken, Task> action,
        Func<Task>? restore = null
    )
    {
        await prepare();
        try
        {
            using (var warm = _serviceProvider.CreateScope())
                await action(warm.ServiceProvider, CancellationToken.None);
            await prepare();

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
                        + $"{Profile.Metadata:N0} metadata / {Profile.WorkQueue:N0} work queue / "
                        + $"{Profile.DeadLetter:N0} dead letter rows"
                );
            return sw.Elapsed;
        }
        finally
        {
            await (restore ?? prepare)();
        }
    }

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
