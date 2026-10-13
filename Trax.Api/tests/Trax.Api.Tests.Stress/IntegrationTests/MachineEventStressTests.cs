using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Mutations;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Effect.StateMachine.Rules;
using OperationResult = HotChocolate.Execution.OperationResult;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// What one typed machine event costs: one run recorded for the internal advance train, one audit entry, and one
/// change notice on <c>trax_snapshot_changed</c>, each counted over a few hundred events, with the time per event
/// bounded. Runs on its own host over the stress database; it seeds nothing, because an event's cost is per row.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[Category("Stress")]
[Explicit("Stress suite. Run with dotnet test --filter TestCategory=Stress")]
public class MachineEventStressTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    /// <summary>The change feed's channel; a notice carries only the row id.</summary>
    private const string Channel = "trax_snapshot_changed";

    private const int Events = 200;

    /// <summary>The mean an event may take through the whole pipeline, write and notice included.</summary>
    private static readonly TimeSpan PerEventBudget = TimeSpan.FromMilliseconds(40);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("TRAX_STRESS_CONNECTION")
        ?? $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress;Username=trax;Password=trax123;"
            + "Maximum Pool Size=16;Timeout=30;Command Timeout=1200;Include Error Detail=true";

    private IHost _host = null!;
    private IRequestExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        BulkSeeder.EnsureDatabaseExists(ConnectionString);
        _host = new HostBuilder()
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
                services.AddSingleton<IHttpContextAccessor>(
                    new MachineChangedStressTests.FixedHttpContext()
                );
                // The operations queries keep the root query non-empty until the machines root exists.
                services.AddScoped<IOperationsService>(sp => new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    sp.GetRequiredService<IDataContextProviderFactory>(),
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>()
                ));
                services.AddScoped(_ => MachineChangedStressTests.Unused<IJobSubmitter>.Create());
                services.AddScoped(_ => MachineChangedStressTests.Unused<ITraxScheduler>.Create());
                services.AddTraxGraphQL(graphql =>
                    graphql
                        .ExposeOperationQueries()
                        .AllowAnonymousOperations()
                        .AddAudit<CountingAuditSink>()
                        .UsePersistedOperations(persisted =>
                            persisted
                                .SingleNode()
                                .RequirePersisted(false)
                                .LogNonPersistedRequests(true)
                                .ExposeOperationsNamespace(false)
                        )
                );
            })
            .Build();
        await _host.StartAsync();
        await ClearToggleDraftsAsync(ConnectionString);
        _executor = await _host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Test]
    public async Task Per_event_cost_is_one_metadata_row_one_audit_entry_and_one_notice()
    {
        var auditsBeforeStart = CountingAuditSink.Entries;
        var id = await StartAsync();
        // The writer delivers in the background: the start's own entry lands before the events are counted.
        (await UntilAsync(() => Task.FromResult(CountingAuditSink.Entries > auditsBeforeStart)))
            .Should()
            .BeTrue("the start is audited");

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

        var runsBefore = await AdvanceRunsAsync();
        var auditsBefore = CountingAuditSink.Entries;

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Events; i++)
            await EventAsync(id, i % 2 == 0 ? "close" : "open");
        clock.Stop();

        // Notices arrive on the listening connection only while it waits on the server.
        (
            await UntilAsync(async () =>
            {
                await listener.WaitAsync(TimeSpan.FromMilliseconds(50));
                return Volatile.Read(ref notices) >= Events;
            })
        )
            .Should()
            .BeTrue($"each event sends a notice on {Channel}, per {Adr}");
        (
            await UntilAsync(() =>
                Task.FromResult(CountingAuditSink.Entries - auditsBefore >= Events)
            )
        )
            .Should()
            .BeTrue("each event's request is audited once");

        var perEvent = clock.Elapsed / Events;
        TestContext.Out.WriteLine(
            $"{Events} events: {clock.Elapsed.TotalMilliseconds:F0} ms, {perEvent.TotalMilliseconds:F2} ms each; "
                + $"runs {await AdvanceRunsAsync() - runsBefore}, audits {CountingAuditSink.Entries - auditsBefore}, "
                + $"notices {Volatile.Read(ref notices)}"
        );

        (await AdvanceRunsAsync() - runsBefore)
            .Should()
            .Be(Events, "each event records one run of the internal advance train, per " + Adr);
        (CountingAuditSink.Entries - auditsBefore).Should().Be(Events, "one audit entry per event");
        Volatile
            .Read(ref notices)
            .Should()
            .Be(Events, "one notice per event, coalesced to none fewer");
        perEvent.Should().BeLessThan(PerEventBudget, "an event's cost is bounded");
    }

    private async Task<Guid> StartAsync()
    {
        var data = await ExecuteAsync(
            $"mutation {{ machines {{ stressToggle {{ start(input: {{ label: \"stress\" }}, requestId: \"{Guid.NewGuid():N}\") {{ id problem {{ code message }} }} }} }} }}"
        );
        var start = data.GetProperty("machines").GetProperty("stressToggle").GetProperty("start");
        if (start.GetProperty("problem").ValueKind == JsonValueKind.Object)
            Assert.Fail($"start was refused: {start.GetProperty("problem").GetRawText()}");
        return Guid.Parse(start.GetProperty("id").GetString()!);
    }

    private Task<JsonElement> EventAsync(Guid id, string field) =>
        ExecuteAsync(
            $"mutation {{ machines {{ stressToggle {{ {field}(id: \"{id}\", requestId: \"{Guid.NewGuid():N}\") {{ revision problem {{ code }} }} }} }} }}"
        );

    private async Task<JsonElement> ExecuteAsync(string document)
    {
        var result = await _executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(document)
                .SetGlobalState(
                    nameof(ClaimsPrincipal),
                    new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, FixedPrincipal.User)],
                            "Stress"
                        )
                    )
                )
                .Build()
        );
        var response = JsonDocument.Parse(((OperationResult)result).ToJson()).RootElement.Clone();
        if (response.TryGetProperty("errors", out var errors))
            Assert.Fail($"The request failed: {errors[0].GetProperty("message").GetString()}");
        return response.GetProperty("data");
    }

    /// <summary>Deletes the toggles an earlier run left in the stress database, so every run starts from none.</summary>
    internal static async Task ClearToggleDraftsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM trax.snapshot_draft WHERE machine = 'stress-toggle'",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> AdvanceRunsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM trax.metadata WHERE name = @name",
            connection
        );
        command.Parameters.AddWithValue("name", typeof(IAdvanceSnapshot).FullName!);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> UntilAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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

    private sealed class FixedPrincipal : ISnapshotPrincipal
    {
        public const string User = "stress-user";

        public string? CurrentUserKey => User;
    }

    /// <summary>Counts every audit entry the writer delivers.</summary>
    public sealed class CountingAuditSink : ITraxAuditSink
    {
        private static long _entries;

        public static long Entries => Interlocked.Read(ref _entries);

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Interlocked.Add(ref _entries, batch.Count);
            return Task.CompletedTask;
        }
    }
}

public enum StressToggleState
{
    Open,
    Closed,
}

public enum StressToggleTrigger
{
    Close,
    Open,
}

public sealed record StressToggleContext
{
    public string? Label { get; init; }
}

public sealed record StressToggleStartInput
{
    public string Label { get; init; } = "";
}

/// <summary>Two states and two events, each any signed-in user's: the cheapest machine an event can move.</summary>
[TraxMachine]
public sealed class StressToggleMachine : Machine<StressToggleState, StressToggleTrigger>
{
    protected override void Configure(IMachineBuilder<StressToggleState, StressToggleTrigger> m)
    {
        m.Id("stress-toggle").Version(1).StartsAt(StressToggleState.Open, () => new JsonObject());
        m.Start<StressToggleStartInput>(s =>
            s.Reduce(
                    Set((StressToggleContext c) => c.Label)
                        .FromInput((StressToggleStartInput i) => i.Label)
                )
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StressToggleState.Open)
            .Context<StressToggleContext>()
            .On(StressToggleTrigger.Close)
            .AnyAuthenticated()
            .To(StressToggleState.Closed);
        m.In(StressToggleState.Closed)
            .Context<StressToggleContext>()
            .On(StressToggleTrigger.Open)
            .AnyAuthenticated()
            .To(StressToggleState.Open);
    }
}
