using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Tests.Stress.Fakes.Trains;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Services.ChangeSignal;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// Fan-out SLAs for the two subscription paths the dashboard keeps open: <c>onDataChanged</c>,
/// fed by every admin write through the change-signal coalescer, and the lifecycle stream
/// (<c>onTrainStateChanged</c>), fed by every run's state changes.
/// </summary>
/// <remarks>
/// <para>
/// The load here is subscribers and publish rate, not rows, so the fixture seeds nothing and
/// builds its own container: the real <c>AddTrax</c> change-signal channel and coalescer, and the
/// real <c>trax</c> schema with its in-memory topic sender. Subscribers attach through the
/// request executor, which is the same subscription pipeline a WebSocket connection reaches
/// after its handshake. <c>TRAX_STRESS_SUBSCRIBERS</c> sets how many (default 1,000).
/// </para>
/// <para>
/// ExecuteAsync returns before the topic subscription is registered, so every subscriber is
/// first confirmed live with a primer event, and the measurement starts only after that.
/// </para>
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class SubscriptionStressTests
{
    private static readonly int SubscriberCount = (int)
        Math.Max(
            1,
            long.TryParse(Environment.GetEnvironmentVariable("TRAX_STRESS_SUBSCRIBERS"), out var n)
                ? n
                : 1_000
        );

    /// <summary>Upper bound on waiting for subscribers to attach or for delivery to finish.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    private ServiceProvider _provider = null!;
    private IRequestExecutor _executor = null!;
    private ChangeSignalCoalescer _coalescer = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var services = new ServiceCollection();
        services
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Warning)
                            // Nothing here reaches the database: the connection is never opened.
                            .UsePostgres(
                                $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress;"
                                    + "Username=trax;Password=trax123"
                            )
                            .AddJson()
                    )
                    .AddMediator(typeof(StressProbeTrain).Assembly)
            );
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql =>
                graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .AllowAnonymousOperations()
        );

        _provider = services.BuildServiceProvider();
        _executor = await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        // A plain ServiceProvider starts no hosted services, so the coalescer is started here.
        _coalescer = _provider
            .GetServices<IHostedService>()
            .OfType<ChangeSignalCoalescer>()
            .Single();
        await _coalescer.StartAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _coalescer.StopAsync(CancellationToken.None);
        _coalescer.Dispose();
        await _provider.DisposeAsync();
    }

    [Test]
    public async Task OnDataChanged_WriteStorm_ReachesEverySubscriberCoalesced()
    {
        // A storm of admin writes across every domain: far more signals than the channel holds,
        // from several writers at once, the way a dispatch cycle and an operator's batch action
        // overlap.
        const int Writers = 8;
        const int SignalsPerWriter = 25_000;
        var domains = Enum.GetValues<ChangeDomain>();

        await using var subscribers = await Subscribers<ChangeDomain>.AttachAsync(
            _executor,
            "subscription { onDataChanged { domain timestamp } }",
            SubscriberCount,
            json => Enum.Parse<ChangeDomain>(Pascal(StringField(json, "domain"))),
            prime: () =>
                _provider
                    .GetRequiredService<ITopicEventSender>()
                    .SendAsync(
                        nameof(LifecycleSubscriptions.OnDataChanged),
                        new DataChangedEvent(ChangeDomain.WorkQueue, DateTime.UtcNow)
                    )
                    .AsTask()
        );

        var signal = _provider.GetRequiredService<ITraxChangeSignal>();
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            Enumerable
                .Range(0, Writers)
                .Select(w =>
                    Task.Run(() =>
                    {
                        for (var i = 0; i < SignalsPerWriter; i++)
                            signal.Notify(domains[(w + i) % domains.Length]);
                    })
                )
        );
        var notified = sw.Elapsed;

        var delivered = await subscribers.WaitUntilAsync(
            received => domains.All(received.Contains),
            Deadline
        );
        sw.Stop();

        var events = subscribers.EventCounts();
        TestContext.Out.WriteLine(
            $"onDataChanged: {Writers * SignalsPerWriter:N0} signals notified in "
                + $"{notified.TotalMilliseconds:F0}ms, every one of {SubscriberCount:N0} subscribers had "
                + $"all {domains.Length} domains after {sw.Elapsed.TotalMilliseconds:F0}ms; events "
                + $"per subscriber min {events.Min()} max {events.Max()}; "
                + $"{_provider.GetRequiredService<TraxChangeSignal>().TotalDropped:N0} signals "
                + "dropped at the full channel"
        );

        delivered.Should().BeTrue("every subscriber should hear about every changed domain");
        notified
            .Should()
            .BeLessThan(
                TimeSpan.FromSeconds(1),
                "Notify must never block a write path, however full the channel is"
            );
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        events
            .Max()
            .Should()
            .BeLessThanOrEqualTo(
                domains.Length * 8,
                $"{Writers * SignalsPerWriter:N0} signals coalesce into a few events per domain, "
                    + "not one per write"
            );
    }

    [Test]
    public async Task OnTrainStateChanged_SustainedRuns_ReachEverySubscriberInFull()
    {
        // A busy host: 500 state changes a second (250 short runs, each publishing on start and
        // on completion) for four seconds, published the way the hook publishes them.
        const int Events = 2_000;
        const int PerTick = 50;
        var tick = TimeSpan.FromMilliseconds(100);

        await using var subscribers = await AttachLifecycleSubscribersAsync();

        var sw = Stopwatch.StartNew();
        for (var i = 1; i <= Events; i++)
        {
            await Publish(i);
            if (i % PerTick == 0)
                // allowed-delay: paces the publisher to a fixed event rate; this is the load
                // shape under test, not a wait for a condition.
                await Task.Delay(tick);
        }
        var published = sw.Elapsed;

        var delivered = await subscribers.WaitUntilAsync(
            received => received.Contains(Events),
            Deadline
        );
        var drained = sw.Elapsed - published;

        var events = subscribers.EventCounts();
        TestContext.Out.WriteLine(
            $"onTrainStateChanged (sustained): {Events:N0} events over "
                + $"{published.TotalMilliseconds:F0}ms reached all {SubscriberCount:N0} subscribers "
                + $"{drained.TotalMilliseconds:F0}ms after the last publish; events per subscriber "
                + $"min {events.Min()} max {events.Max()}"
        );

        delivered.Should().BeTrue("the last run's state change should reach every subscriber");
        drained.Should().BeLessThan(TimeSpan.FromSeconds(1));
        events
            .Min()
            .Should()
            .Be(
                Events,
                "at a sustained rate a subscriber that falls behind loses state changes it never "
                    + "sees again, and the dashboard's live feed shows stale runs until it refetches"
            );
    }

    [Test]
    public async Task OnTrainStateChanged_InstantBurst_LastEventReachesEverySubscriber()
    {
        // Many runs changing state at once, such as a batch cancel or a worker pool finishing
        // together. A subscriber's topic buffer is bounded, so under a burst it keeps the newest
        // events and drops older ones: what must hold is that every subscriber ends on the latest
        // state, and quickly. The count each one kept is reported, not asserted.
        const int Events = 2_000;

        await using var subscribers = await AttachLifecycleSubscribersAsync();

        var sw = Stopwatch.StartNew();
        for (var i = 1; i <= Events; i++)
            await Publish(i);
        var published = sw.Elapsed;

        var delivered = await subscribers.WaitUntilAsync(
            received => received.Contains(Events),
            Deadline
        );
        sw.Stop();

        var events = subscribers.EventCounts();
        TestContext.Out.WriteLine(
            $"onTrainStateChanged (burst): {Events:N0} events published in "
                + $"{published.TotalMilliseconds:F0}ms, the last one reached all {SubscriberCount:N0} "
                + $"subscribers after {sw.Elapsed.TotalMilliseconds:F0}ms; events kept per "
                + $"subscriber min {events.Min()} max {events.Max()}"
        );

        delivered.Should().BeTrue("the last run's state change should reach every subscriber");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private Task<Subscribers<long>> AttachLifecycleSubscribersAsync() =>
        Subscribers<long>.AttachAsync(
            _executor,
            "subscription { onTrainStateChanged { metadataId trainName trainState } }",
            SubscriberCount,
            json => long.Parse(NumberField(json, "metadataId")),
            prime: () => Publish(0)
        );

    private Task Publish(long metadataId) =>
        _provider
            .GetRequiredService<ITopicEventSender>()
            .SendAsync(
                nameof(LifecycleSubscriptions.OnTrainStateChanged),
                new TrainLifecycleEvent(
                    metadataId,
                    metadataId.ToString("D32"),
                    typeof(IStressProbeTrain).FullName!,
                    TrainState.Completed,
                    DateTime.UtcNow,
                    FailureJunction: null,
                    FailureReason: null,
                    Output: null
                )
            )
            .AsTask();

    /// <summary>The value of the first string field named <paramref name="name"/>.</summary>
    private static string StringField(string json, string name)
    {
        var start = json.IndexOf($"\"{name}\"", StringComparison.Ordinal);
        start = json.IndexOf('"', json.IndexOf(':', start) + 1) + 1;
        return json[start..json.IndexOf('"', start)];
    }

    /// <summary>The digits of the first number field named <paramref name="name"/>.</summary>
    private static string NumberField(string json, string name)
    {
        var start = json.IndexOf($"\"{name}\"", StringComparison.Ordinal);
        start = json.IndexOf(':', start) + 1;
        while (!char.IsAsciiDigit(json[start]))
            start++;
        var end = start;
        while (char.IsAsciiDigit(json[end]))
            end++;
        return json[start..end];
    }

    /// <summary><c>WORK_QUEUE</c> to <c>WorkQueue</c>.</summary>
    private static string Pascal(string constantCase) =>
        string.Concat(
            constantCase.Split('_').Select(part => part[..1] + part[1..].ToLowerInvariant())
        );

    /// <summary>
    /// A set of live subscription streams, each drained by its own reader into what it has
    /// received since the last <see cref="Reset"/>.
    /// </summary>
    private sealed class Subscribers<T> : IAsyncDisposable
        where T : notnull
    {
        private readonly List<IResponseStream> _streams = [];
        private readonly List<Task> _readers = [];
        private readonly ConcurrentDictionary<int, ConcurrentBag<T>> _received = new();

        public static async Task<Subscribers<T>> AttachAsync(
            IRequestExecutor executor,
            string subscription,
            int count,
            Func<string, T> read,
            Func<Task> prime
        )
        {
            var subscribers = new Subscribers<T>();
            for (var i = 0; i < count; i++)
            {
                var result = await executor.ExecuteAsync(subscription);
                var stream = result.ExpectResponseStream();
                var index = i;
                subscribers._received[index] = [];
                subscribers._streams.Add(stream);
                subscribers._readers.Add(
                    Task.Run(async () =>
                    {
                        await foreach (var item in stream.ReadResultsAsync())
                        {
                            // Serializing each event per subscriber is the work a socket
                            // does too; the field is then read without a second parse, so
                            // the readers are not the bottleneck being measured.
                            subscribers._received[index].Add(read(item.ToJson()));
                        }
                    })
                );
            }

            // Publish primers until every subscriber has one, then start clean.
            var deadline = Stopwatch.StartNew();
            while (subscribers._received.Values.Any(b => b.IsEmpty) && deadline.Elapsed < Deadline)
            {
                await prime();
                // allowed-delay: poll interval while subscriptions register, bounded by Deadline.
                await Task.Delay(50);
            }
            subscribers
                ._received.Values.Should()
                .OnlyContain(b => !b.IsEmpty, "every subscriber must be live before measuring");
            subscribers.Reset();
            return subscribers;
        }

        public void Reset()
        {
            foreach (var key in _received.Keys)
                _received[key] = [];
        }

        public int[] EventCounts() => _received.Values.Select(b => b.Count).ToArray();

        public async Task<bool> WaitUntilAsync(
            Func<IReadOnlyCollection<T>, bool> done,
            TimeSpan timeout
        )
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (_received.Values.All(b => done(b)))
                    return true;
                // allowed-delay: completion poll, bounded by the caller's timeout.
                await Task.Delay(10);
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in _streams)
                await stream.DisposeAsync();
            await Task.WhenAll(_readers);
        }
    }
}
