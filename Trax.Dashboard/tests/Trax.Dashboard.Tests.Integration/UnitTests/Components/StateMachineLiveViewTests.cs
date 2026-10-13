using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The state-machine instance page is live: it follows <see cref="IOperationsService.WatchMachineInstancesAsync"/>,
/// the feed the API's operator subscription reads, so a write shows without waiting for a poll; the page and GraphQL
/// read one feed and show the same sequence; leaving the page ends its subscription; and a circuit whose operator
/// loses the role stops receiving within a bounded time.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class StateMachineLiveViewTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Machine = "Acme.Fulfilment";
    private const string OperatorRole = "Operator";
    private const string Watch = nameof(IOperationsService.WatchMachineInstancesAsync);

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private OperationsCallLog _calls = null!;
    private FakeFeed _feed = null!;

    [SetUp]
    public async Task SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());

        // No poll tick happens during a test: whatever the page shows past its first load came from the feed.
        var storage = new InMemoryLocalStorageService();
        await storage.SetAsync<int?>(StorageKeys.PollingInterval, 3600);
        _ctx.Services.AddSingleton<ILocalStorageService>(storage);

        _feed = new FakeFeed();
        _calls = RecordingOperationsService.Register(_ctx.Services, _data);
        _calls.Respond = (name, args) =>
            name == Watch ? _feed.Watch((string?)args[0], (CancellationToken)args[1]!) : null;
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_instance_page_updates_from_the_feed_without_a_poll_tick()
    {
        var row = await SeedAsync("Running");
        var page = RenderDetail(row.Id);
        page.WaitForAssertion(() => page.Markup.Should().Contain("Running"), WaitTimeout);
        var loads = _calls.CallsTo(nameof(IOperationsService.GetMachineInstanceAsync)).Count;

        page.WaitForAssertion(
            () =>
                _calls.CallsTo(Watch).Should().NotBeEmpty("the page follows the feed, per " + Adr),
            WaitTimeout
        );
        _feed.Publish(Record(row, "Shipping", revision: 5));

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("Shipping", "the write shows as it happens"),
            WaitTimeout
        );
        _calls
            .CallsTo(nameof(IOperationsService.GetMachineInstanceAsync))
            .Should()
            .HaveCount(loads, "the page did not reload to see it");
    }

    [Test]
    public async Task A_reload_on_the_feed_reads_the_instance_again()
    {
        var row = await SeedAsync("Running");
        var page = RenderDetail(row.Id);
        page.WaitForAssertion(() => page.Markup.Should().Contain("Running"), WaitTimeout);
        page.WaitForAssertion(() => _calls.CallsTo(Watch).Should().NotBeEmpty(), WaitTimeout);
        var loads = _calls.CallsTo(nameof(IOperationsService.GetMachineInstanceAsync)).Count;

        _feed.Publish(Record(row, "", revision: 0) with { Reload = true });

        page.WaitForAssertion(
            () =>
                _calls
                    .CallsTo(nameof(IOperationsService.GetMachineInstanceAsync))
                    .Count.Should()
                    .BeGreaterThan(
                        loads,
                        "writes may have been missed while the listener was away, so the page reads the "
                            + "instance again, per "
                            + Adr
                    ),
            WaitTimeout
        );
    }

    [Test]
    public async Task The_page_and_GraphQL_read_one_feed_service()
    {
        var row = await SeedAsync("Packing");
        var page = RenderDetail(row.Id);
        page.WaitForAssertion(() => page.Markup.Should().Contain("Packing"), WaitTimeout);

        await using var graphql = BuildGraphQL(
            RecordingOperationsService.Wrap(UnusedService<IOperationsService>.Create(), _calls)
        );
        var executor = await graphql
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        var result = await executor.ExecuteAsync(
            "subscription { onMachineInstanceChanged { id state revision } }"
        );
        result
            .Should()
            .BeAssignableTo<IResponseStream>(
                "the operator feed is a subscription: "
                    + (
                        result is HotChocolate.Execution.OperationResult answer
                            ? answer.ToJson().ReplaceLineEndings(" ")
                            : ""
                    )
            );
        await using var stream = (IResponseStream)result;
        using var reading = new CancellationTokenSource(WaitTimeout);
        var seenByGraphQL = new List<string>();
        var graphqlReads = Task.Run(async () =>
        {
            await foreach (var next in stream.ReadResultsAsync().WithCancellation(reading.Token))
            {
                seenByGraphQL.Add(next.ToJson());
                if (seenByGraphQL.Count == 2)
                    return;
            }
        });
        page.WaitForAssertion(
            () =>
                _calls
                    .CallsTo(Watch)
                    .Should()
                    .HaveCount(
                        2,
                        "the page and the subscription each follow the one feed, per " + Adr
                    ),
            WaitTimeout
        );

        _feed.Publish(Record(row, "Packed", revision: 2));
        _feed.Publish(Record(row, "Shipped", revision: 3));
        await graphqlReads.WaitAsync(WaitTimeout);

        seenByGraphQL.Should().HaveCount(2);
        seenByGraphQL[0].Should().Contain("Packed");
        seenByGraphQL[1].Should().Contain("Shipped");
        page.WaitForAssertion(
            () =>
                page
                    .Markup.Should()
                    .Contain("Shipped", "the page shows where the same sequence ended"),
            WaitTimeout
        );
    }

    [Test]
    public async Task Leaving_the_page_disposes_its_feed_subscription()
    {
        var row = await SeedAsync("Running");
        var page = RenderDetail(row.Id);
        page.WaitForAssertion(
            () => _feed.Opened.Should().Be(1, "the page follows the feed, per " + Adr),
            WaitTimeout
        );

        _ctx.DisposeComponents();

        page.WaitForAssertion(
            () => _feed.Closed.Should().Be(1, "leaving the page ends its subscription"),
            WaitTimeout
        );
    }

    [Test]
    public async Task A_circuit_whose_operator_loses_the_role_stops_receiving_within_the_bounded_lifetime()
    {
        _ctx.Services.AddSingleton(new DashboardOptions().RequireRoles(OperatorRole));
        // The circuit's re-check of the dashboard's posture, as AddTraxDashboard registers it.
        _ctx.Services.AddScoped<DashboardCircuitAuthorization>();
        var auth = _ctx.AddTestAuthorization();
        auth.SetAuthorized("operator");
        auth.SetRoles(OperatorRole);
        var row = await SeedAsync("Running");
        var page = RenderDetail(row.Id);
        page.WaitForAssertion(() => _feed.Opened.Should().Be(1, "per " + Adr), WaitTimeout);
        _feed.Publish(Record(row, "Packing", revision: 2));
        page.WaitForAssertion(() => page.Markup.Should().Contain("Packing"), WaitTimeout);

        auth.SetRoles();

        page.WaitForAssertion(
            () =>
                _feed
                    .Closed.Should()
                    .Be(
                        1,
                        "the circuit re-checks its operator and stops listening once the role is gone"
                    ),
            WaitTimeout
        );
        _feed.Publish(Record(row, "Shipped", revision: 3));
        page.Markup.Should().NotContain("Shipped", "nothing reaches a circuit that lost the role");
    }

    private IRenderedComponent<StateMachineInstancePage> RenderDetail(Guid id)
    {
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(
            $"trax/data/state-machines/{Uri.EscapeDataString(Machine)}/system/{id}"
        );
        return _ctx.RenderComponent<StateMachineInstancePage>(p =>
            p.Add(x => x.Machine, Machine).Add(x => x.Owner, "system").Add(x => x.InstanceId, id)
        );
    }

    private async Task<SnapshotDraft> SeedAsync(string state)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var now = DateTimeOffset.UtcNow;
        var row = new SnapshotDraft
        {
            Id = Guid.NewGuid(),
            OwnerKind = SnapshotOwnerKind.System,
            Machine = Machine,
            Version = 1,
            State = state,
            Context = "{}",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now,
        };
        db.SnapshotDrafts.Add(row);
        await db.SaveChanges(default);
        return row;
    }

    private static MachineInstanceRecord Record(SnapshotDraft row, string state, long revision) =>
        new(
            row.RowId,
            row.Machine,
            row.OwnerKind,
            row.Id,
            state,
            row.Version,
            row.CreatedAt,
            DateTimeOffset.UtcNow,
            HasLiveInvokedRun: false
        )
        {
            Revision = revision,
        };

    // The API's GraphQL schema over the given operations service, with the operations namespace open.
    private static ServiceProvider BuildGraphQL(IOperationsService operations)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton<ITrainDiscoveryService>(
            new TrainDiscoveryService(new ServiceCollection())
        );
        services.AddSingleton(UnusedService<IEffectRegistry>.Create());
        services.AddSingleton(operations);
        services.AddSingleton(UnusedService<ITraxScheduler>.Create());
        services.AddSingleton(UnusedService<ITraxHealthService>.Create());
        services.AddSingleton(UnusedService<ITrainExecutionService>.Create());
        services.AddTraxGraphQL(graphql =>
            graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The operator feed, driven by the test: each call to <see cref="Watch"/> opens a stream, and
    /// <see cref="Publish"/> sends a record to every open one.
    /// </summary>
    private sealed class FakeFeed
    {
        private readonly List<Channel<MachineInstanceRecord>> _open = [];
        private int _opened;
        private int _closed;

        /// <summary>Streams opened so far.</summary>
        public int Opened => Volatile.Read(ref _opened);

        /// <summary>Streams ended so far, by cancellation or disposal.</summary>
        public int Closed => Volatile.Read(ref _closed);

        public void Publish(MachineInstanceRecord record)
        {
            lock (_open)
                foreach (var channel in _open)
                    channel.Writer.TryWrite(record);
        }

        public async IAsyncEnumerable<MachineInstanceRecord> Watch(
            string? machine,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            var channel = Channel.CreateUnbounded<MachineInstanceRecord>();
            lock (_open)
                _open.Add(channel);
            Interlocked.Increment(ref _opened);
            try
            {
                await foreach (var record in channel.Reader.ReadAllAsync(ct))
                    if (machine is null || record.Machine == machine)
                        yield return record;
            }
            finally
            {
                lock (_open)
                    _open.Remove(channel);
                Interlocked.Increment(ref _closed);
            }
        }
    }
}
