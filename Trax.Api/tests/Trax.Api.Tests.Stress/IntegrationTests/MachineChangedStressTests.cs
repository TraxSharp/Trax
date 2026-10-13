using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Services.PostgresContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using OperationResult = HotChocolate.Execution.OperationResult;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// <c>machineChanged</c> under load: a thousand subscribers on one host, one per instance, while another host makes
/// ten thousand writes. The subscribing host reads the database at most once per notice, every subscriber ends on
/// its instance's last revision, the time from a write to its event stays bounded, and the host's memory does not
/// grow with the number of writes.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[Category("Stress")]
[Explicit("Stress suite. Run with dotnet test --filter TestCategory=Stress")]
public class MachineChangedStressTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Channel = "trax_snapshot_changed";
    private const int Subscribers = 1_000;
    private const int WritesPerInstance = 10;

    /// <summary>The slowest a write may take to reach its subscriber, at the 99th percentile.</summary>
    private static readonly TimeSpan LatencyBudget = TimeSpan.FromSeconds(2);

    /// <summary>How much the subscribing host's managed heap may grow over the run.</summary>
    private const long MemoryBudgetBytes = 512L * 1024 * 1024;

    /// <summary>The longest the ten thousand writes may take; they take seconds.</summary>
    private static readonly TimeSpan WritesBudget = TimeSpan.FromMinutes(2);

    /// <summary>The longest a subscription may take to end once it is disposed, and the hosts to stop.</summary>
    private static readonly TimeSpan EndBudget = TimeSpan.FromSeconds(30);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("TRAX_STRESS_CONNECTION")
        ?? $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress;Username=trax;Password=trax123;"
            + "Maximum Pool Size=32;Timeout=30;Command Timeout=1200;Include Error Detail=true";

    private IHost _writer = null!;
    private IHost _subscriber = null!;
    private IRequestExecutor _writes = null!;
    private IRequestExecutor _subscriptions = null!;
    private ReadCounter _reads = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        BulkSeeder.EnsureDatabaseExists(ConnectionString);
        _writer = await StartAsync(counter: null);
        await MachineEventStressTests.ClearToggleDraftsAsync(ConnectionString);
        _reads = new ReadCounter();
        _subscriber = await StartAsync(_reads);
        _writes = await ExecutorAsync(_writer);
        _subscriptions = await ExecutorAsync(_subscriber);
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        foreach (var host in new[] { _subscriber, _writer })
        {
            using var stopping = new CancellationTokenSource(EndBudget);
            await host.StopAsync(stopping.Token);
            host.Dispose();
        }
    }

    [Test]
    public async Task A_thousand_subscribers_and_ten_thousand_writes_stay_bounded()
    {
        // One instance started through the API, the rest written beside it for the same user: the test is about
        // the push, not about starting.
        var first = await StartAsync();
        var instances = new List<Guid> { first };
        await using (var seed = new NpgsqlConnection(ConnectionString))
        {
            await seed.OpenAsync();
            await using var copy = new NpgsqlCommand(
                "INSERT INTO trax.snapshot_draft (id, user_key, machine, version, state, context, "
                    + "concurrency_token, updated_at, created_at, owner_kind) "
                    + "SELECT gen_random_uuid(), user_key, machine, version, state, context, gen_random_uuid(), "
                    + "now(), now(), owner_kind FROM trax.snapshot_draft, generate_series(1, @n) "
                    + "WHERE machine = 'stress-toggle' AND id = @id RETURNING id",
                seed
            );
            copy.Parameters.AddWithValue("n", Subscribers - 1);
            copy.Parameters.AddWithValue("id", first);
            await using var read = await copy.ExecuteReaderAsync();
            while (await read.ReadAsync())
                instances.Add(read.GetGuid(0));
        }

        await using var listener = new NpgsqlConnection(ConnectionString);
        await listener.OpenAsync();
        var notices = 0;
        listener.Notification += (_, e) =>
        {
            if (e.Channel == Channel)
                Interlocked.Increment(ref notices);
        };
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", listener))
            await listen.ExecuteNonQueryAsync();

        // Each subscriber records its latest revision and when each revision arrived.
        var latest = new ConcurrentDictionary<Guid, long>();
        var arrived = new ConcurrentDictionary<(Guid, long), long>();
        var streams = new List<IResponseStream>();
        using var stop = new CancellationTokenSource();
        var readers = new List<Task>();
        foreach (var id in instances)
        {
            var stream = await SubscribeAsync(id);
            streams.Add(stream);
            readers.Add(
                Task.Run(async () =>
                {
                    await foreach (
                        var result in stream.ReadResultsAsync().WithCancellation(stop.Token)
                    )
                    {
                        var revision = JsonDocument
                            .Parse(result.ToJson())
                            .RootElement.GetProperty("data")
                            .GetProperty("machineChanged")
                            .GetProperty("revision")
                            .GetInt64();
                        latest[id] = revision;
                        arrived.TryAdd((id, revision), Stopwatch.GetTimestamp());
                    }
                })
            );
        }

        TestContext.Progress.WriteLine($"{streams.Count} subscriptions open");
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var readsBefore = _reads.Count;
        var written = new ConcurrentDictionary<(Guid, long), long>();
        var finalRevision = new ConcurrentDictionary<Guid, long>();
        // Every write in flight, with when it was sent, so a write that never answers is named rather than waited
        // on. Holding its task also keeps what it awaits reachable, so a hang dump shows where it stopped.
        var inFlight =
            new ConcurrentDictionary<(Guid Id, int Write), (long Sent, Task<long> Write)>();
        var writes = Parallel.ForEachAsync(
            instances,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (id, cancellationToken) =>
            {
                for (var i = 0; i < WritesPerInstance; i++)
                {
                    var write = EventAsync(id, i % 2 == 0 ? "close" : "open");
                    inFlight[(id, i)] = (Stopwatch.GetTimestamp(), write);
                    var revision = await write;
                    inFlight.TryRemove((id, i), out _);
                    written[(id, revision)] = Stopwatch.GetTimestamp();
                    finalRevision[id] = revision;
                }
            }
        );
        if (!await FinishesWithinAsync(writes, WritesBudget))
        {
            var stuck = await StoredRevisionsAsync(inFlight.Keys.Select(w => w.Id).Take(20));
            Assert.Fail(
                $"The writes did not finish within {WritesBudget}: {inFlight.Count} never answered: "
                    + string.Join(
                        ", ",
                        inFlight
                            .Take(20)
                            .Select(w =>
                                $"{w.Key.Id} write {w.Key.Write} "
                                + $"({Stopwatch.GetElapsedTime(w.Value.Sent).TotalSeconds:F0} s ago, "
                                + $"stored revision {stuck.GetValueOrDefault(w.Key.Id, -1)})"
                            )
                    )
            );
        }
        await writes;
        TestContext.Progress.WriteLine("every write answered");

        if (
            !await UntilAsync(() =>
                Task.FromResult(
                    instances.All(id =>
                        latest.TryGetValue(id, out var seen) && seen == finalRevision[id]
                    )
                )
            )
        )
        {
            var behind = instances
                .Where(id => !latest.TryGetValue(id, out var seen) || seen != finalRevision[id])
                .ToList();
            var stored = await StoredRevisionsAsync(behind.Take(20));
            Assert.Fail(
                $"{behind.Count} of {instances.Count} subscribers never reached their instance's last revision, per "
                    + Adr
                    + ": "
                    + string.Join(
                        ", ",
                        behind
                            .Take(20)
                            .Select(id =>
                                $"{id} saw {(latest.TryGetValue(id, out var seen) ? seen : -1)}, "
                                + $"last written {finalRevision[id]}, stored {stored.GetValueOrDefault(id, -1)}"
                            )
                    )
            );
        }
        TestContext.Progress.WriteLine("every subscriber is on its last revision");

        // Notices arrive on the listening connection only while it waits on the server, one per wait: drain them
        // all on every poll.
        var expectedNotices = Subscribers * WritesPerInstance;
        (
            await UntilAsync(async () =>
            {
                while (await listener.WaitAsync(TimeSpan.FromMilliseconds(50))) { }
                return Volatile.Read(ref notices) >= expectedNotices;
            })
        ).Should().BeTrue($"each write sends a notice on {Channel}; {Volatile.Read(ref notices)} arrived");

        var reads = _reads.Count - readsBefore;
        var latencies = written
            .Where(w => arrived.ContainsKey(w.Key))
            .Select(w => Stopwatch.GetElapsedTime(w.Value, arrived[w.Key]))
            .OrderBy(t => t)
            .ToList();
        var p99 = latencies[(int)(latencies.Count * 0.99)];
        GC.Collect();
        var memoryGrowth = GC.GetTotalMemory(forceFullCollection: true) - memoryBefore;
        TestContext.Out.WriteLine(
            $"{Subscribers} subscribers, {expectedNotices} writes: notices {Volatile.Read(ref notices)}, "
                + $"subscriber reads {reads}, p99 {p99.TotalMilliseconds:F0} ms over {latencies.Count} events, "
                + $"heap growth {memoryGrowth / 1024 / 1024} MiB"
        );

        reads
            .Should()
            .BeLessThanOrEqualTo(
                Volatile.Read(ref notices),
                "the subscribing host reads a row at most once per notice, whatever its subscribers"
            );
        p99.Should().BeLessThan(LatencyBudget, "a write reaches its subscriber promptly");
        memoryGrowth
            .Should()
            .BeLessThan(MemoryBudgetBytes, "the host keeps no history of what it sent");

        TestContext.Progress.WriteLine("ending the streams");
        await stop.CancelAsync();
        foreach (var stream in streams)
        {
            if (!await FinishesWithinAsync(stream.DisposeAsync().AsTask(), EndBudget))
                Assert.Fail(
                    $"A subscription stream did not end within {EndBudget} of its disposal."
                );
        }
        var ended = Task.WhenAll(readers.Select(r => r.ContinueWith(_ => { })));
        if (!await FinishesWithinAsync(ended, EndBudget))
            Assert.Fail(
                $"{readers.Count(r => !r.IsCompleted)} subscriber readers did not end within {EndBudget} of "
                    + "their streams' disposal."
            );
    }

    /// <summary>Whether <paramref name="task"/> completes within <paramref name="budget"/>; it is not awaited.</summary>
    // allowed-delay: the budget bounds a wait on the task; it is never awaited on its own.
    private static async Task<bool> FinishesWithinAsync(Task task, TimeSpan budget) =>
        await Task.WhenAny(task, Task.Delay(budget)) == task;

    /// <summary>The revision the database holds for each of <paramref name="ids"/>.</summary>
    private static async Task<Dictionary<Guid, long>> StoredRevisionsAsync(IEnumerable<Guid> ids)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id, revision FROM trax.snapshot_draft WHERE id = ANY(@ids)",
            connection
        );
        command.Parameters.AddWithValue("ids", ids.ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var revisions = new Dictionary<Guid, long>();
        while (await reader.ReadAsync())
            revisions[reader.GetGuid(0)] = reader.GetInt64(1);
        return revisions;
    }

    private static async Task<IHost> StartAsync(ReadCounter? counter)
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
                services.AddAuthorization();
                services.AddTrax(trax =>
                    trax.AddEffects(effects =>
                            effects
                                .SetEffectLogLevel(LogLevel.Warning)
                                .UsePostgres(ConnectionString)
                                .AddJson()
                        )
                        .AddStateMachines(typeof(StressToggleMachine).Assembly)
                        .AddMediator(typeof(StressToggleMachine).Assembly)
                );
                services.AddSingleton<ISnapshotPrincipal>(new FixedPrincipal());
                services.AddTraxApi();
                // The machine trains authorize the request's HTTP user, which an in-process executor has none of.
                services.AddSingleton<IHttpContextAccessor>(new FixedHttpContext());
                // The operations queries keep the root query non-empty; nothing here runs a train.
                services.AddScoped<IOperationsService>(sp => new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>()
                ));
                services.AddScoped(_ => Unused<IJobSubmitter>.Create());
                services.AddScoped(_ => Unused<ITraxScheduler>.Create());
                services.AddTraxGraphQL(graphql =>
                    graphql
                        .ExposeOperationQueries()
                        .AllowAnonymousOperations()
                        .UsePersistedOperations(persisted =>
                            persisted
                                .SingleNode()
                                .RequirePersisted(false)
                                .LogNonPersistedRequests(true)
                                .ExposeOperationsNamespace(false)
                        )
                );
                if (counter is not null)
                    counter.Install(services);
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    private static Task<IRequestExecutor> ExecutorAsync(IHost host) =>
        host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .AsTask();

    private async Task<Guid> StartAsync()
    {
        var data = await ExecuteAsync(
            _writes,
            $"mutation {{ machines {{ stressToggle {{ start(input: {{ label: \"stress\" }}, requestId: \"{Guid.NewGuid():N}\") {{ id problem {{ code message }} }} }} }} }}"
        );
        var start = data.GetProperty("machines").GetProperty("stressToggle").GetProperty("start");
        if (start.GetProperty("problem").ValueKind == JsonValueKind.Object)
            Assert.Fail($"start was refused: {start.GetProperty("problem").GetRawText()}");
        return Guid.Parse(start.GetProperty("id").GetString()!);
    }

    private async Task<long> EventAsync(Guid id, string field)
    {
        var data = await ExecuteAsync(
            _writes,
            $"mutation {{ machines {{ stressToggle {{ {field}(id: \"{id}\", requestId: \"{Guid.NewGuid():N}\") {{ revision }} }} }} }}"
        );
        return data.GetProperty("machines")
            .GetProperty("stressToggle")
            .GetProperty(field)
            .GetProperty("revision")
            .GetInt64();
    }

    private async Task<IResponseStream> SubscribeAsync(Guid id)
    {
        var result = await _subscriptions.ExecuteAsync(
            Request(
                $"subscription {{ machineChanged(machine: \"stress-toggle\", id: \"{id}\") {{ revision state gone }} }}"
            )
        );
        if (result is OperationResult refused)
            Assert.Fail($"The subscription was refused: {refused.ToJson()}");
        return (IResponseStream)result;
    }

    private static async Task<JsonElement> ExecuteAsync(IRequestExecutor executor, string document)
    {
        var result = await executor.ExecuteAsync(Request(document));
        var response = JsonDocument.Parse(((OperationResult)result).ToJson()).RootElement.Clone();
        if (response.TryGetProperty("errors", out var errors))
            Assert.Fail($"The request failed: {errors[0].GetProperty("message").GetString()}");
        return response.GetProperty("data");
    }

    private static IOperationRequest Request(string document) =>
        OperationRequestBuilder
            .New()
            .SetDocument(document)
            .SetGlobalState(
                nameof(ClaimsPrincipal),
                new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, FixedPrincipal.User)], "Stress")
                )
            )
            .Build();

    private static async Task<bool> UntilAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            while (!await condition())
                // determinism: the poll interval of a wait bounded by the timeout above.
                await Task.Delay(25, deadline.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>A service the host registers but this test never calls: every member throws.</summary>
    public class Unused<T> : DispatchProxy
        where T : class
    {
        public static T Create() => Create<T, Unused<T>>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{typeof(T).Name} is not used by this test.");
    }

    private sealed class FixedPrincipal : ISnapshotPrincipal
    {
        public const string User = "stress-user";

        public string? CurrentUserKey => User;
    }

    /// <summary>An HTTP context whose user is the stress user, signed in, for every request of the host.</summary>
    internal sealed class FixedHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } =
            new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, FixedPrincipal.User)], "Stress")
                ),
            };
    }

    /// <summary>Counts the data contexts a host creates, every path to the database included.</summary>
    private sealed class ReadCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Install(IServiceCollection services)
        {
            var contexts = services.Last(d =>
                d.ServiceType == typeof(IDbContextFactory<PostgresContext>)
            );
            services.Remove(contexts);
            services.Add(
                new ServiceDescriptor(
                    typeof(IDbContextFactory<PostgresContext>),
                    sp => new Counting(
                        (IDbContextFactory<PostgresContext>)(
                            contexts.ImplementationInstance
                            ?? contexts.ImplementationFactory?.Invoke(sp)
                            ?? ActivatorUtilities.CreateInstance(sp, contexts.ImplementationType!)
                        ),
                        this
                    ),
                    contexts.Lifetime
                )
            );
        }

        private sealed class Counting(IDbContextFactory<PostgresContext> inner, ReadCounter counter)
            : IDbContextFactory<PostgresContext>
        {
            public PostgresContext CreateDbContext()
            {
                Interlocked.Increment(ref counter._count);
                return inner.CreateDbContext();
            }
        }
    }
}
