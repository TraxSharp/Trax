using System.Collections.Concurrent;
using System.Diagnostics;
using AwesomeAssertions;
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
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// Fan-out SLAs for the subscription paths the dashboard keeps open: <c>onDataChanged</c>,
/// fed by every admin write through the change-signal coalescer, the lifecycle stream
/// (<c>onTrainStateChanged</c>), fed by every run's state changes, and <c>onJunctionEvent</c>,
/// fed by every run's steps on one shared topic.
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
/// <para>
/// The lifecycle feed is lossy under load and numbers its events so a subscriber can tell; the
/// sustained case holds that contract. Enforces
/// <c>docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Stress")]
[Property("adr", "docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md")]
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
    public async Task OnTrainStateChanged_SustainedRuns_ReachEverySubscriberInFullOrSignalAGap()
    {
        // A busy host: 500 state changes a second (250 short runs, each publishing on start and
        // on completion) for four seconds, published the way the hooks publish them. The feed is
        // lossy by design: a subscriber that falls behind loses its oldest buffered events. What
        // must hold is that it is never silent about it: every subscriber either receives every
        // event, or sees a skip in `sequence` and knows to refetch.
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
            received => received.Any(e => e.MetadataId == Events),
            Deadline
        );
        var drained = sw.Elapsed - published;

        var outcomes = subscribers.Streams().Select(Outcome).ToArray();
        var complete = outcomes.Count(o => o.Received == Events);
        var gapped = outcomes.Count(o => o.Gap);
        TestContext.Out.WriteLine(
            $"onTrainStateChanged (sustained): {Events:N0} events over "
                + $"{published.TotalMilliseconds:F0}ms reached all {SubscriberCount:N0} subscribers "
                + $"{drained.TotalMilliseconds:F0}ms after the last publish; {complete:N0} received "
                + $"all of them, {gapped:N0} saw a gap in sequence; events per subscriber min "
                + $"{outcomes.Min(o => o.Received)} max {outcomes.Max(o => o.Received)}"
        );

        delivered.Should().BeTrue("the last run's state change should reach every subscriber");
        drained.Should().BeLessThan(TimeSpan.FromSeconds(1));
        outcomes
            .Should()
            .OnlyContain(
                o => o.Received == Events || o.Gap,
                "a subscriber that lost state changes must see a skip in sequence, so it knows to "
                    + "refetch, per docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md"
            );
        outcomes
            .Should()
            .OnlyContain(
                o => !o.Gap || o.Received < Events,
                "a subscriber that received every event saw no loss, so it must see no skip"
            );
    }

    /// <summary>
    /// How many measured events one subscriber received, and whether its <c>sequence</c> skipped a
    /// number anywhere from the last primer it saw onward.
    /// </summary>
    private static (int Received, bool Gap) Outcome(
        (IReadOnlyList<Lifecycle> BeforeReset, IReadOnlyList<Lifecycle> Received) stream
    )
    {
        var numbers = stream.BeforeReset.Concat(stream.Received).Select(e => e.Sequence).ToList();
        var gap = numbers.Zip(numbers.Skip(1), (a, b) => b != a + 1).Any(skipped => skipped);
        return (stream.Received.Count, gap);
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
            received => received.Any(e => e.MetadataId == Events),
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

    [Test]
    public async Task OnJunctionEvent_ManyRunsOnTheTopic_EachSubscriberGetsItsRunInFullOrSignalsAGap()
    {
        // Many runs stepping at once on the one junction topic, each followed by its own share of
        // the subscribers: every subscriber reads every run's steps and keeps only its run's, so
        // the fan-out cost is subscribers x all steps, not subscribers x their run's steps. The
        // steps go through the handler the run's path calls, so its publish bound is what is
        // measured on the run's side. Paced at 500 steps a second for four seconds.
        const int Runs = 100;
        const int StepsPerRun = 20;
        const int PerTick = 50;
        var tick = TimeSpan.FromMilliseconds(100);

        var handler = _provider.GetServices<IJunctionEventHandler>().Single();
        var subscribers = new List<Subscribers<Step>>();
        try
        {
            for (var run = 1; run <= Runs; run++)
            {
                var metadataId = run;
                subscribers.Add(
                    await Subscribers<Step>.AttachAsync(
                        _executor,
                        $"subscription {{ onJunctionEvent(metadataId: {metadataId}) "
                            + "{ sequence metadataId junction { position } } }",
                        Math.Max(1, SubscriberCount / Runs),
                        json => new Step(
                            long.Parse(NumberField(json, "position")),
                            long.Parse(NumberField(json, "sequence"))
                        ),
                        prime: () => HandleStep(handler, metadataId, position: 0)
                    )
                );
            }

            var slowest = TimeSpan.Zero;
            var sw = Stopwatch.StartNew();
            var sent = 0;
            for (var position = 1; position <= StepsPerRun; position++)
            for (var run = 1; run <= Runs; run++)
            {
                var publish = Stopwatch.StartNew();
                await HandleStep(handler, run, position);
                if (publish.Elapsed > slowest)
                    slowest = publish.Elapsed;
                if (++sent % PerTick == 0)
                    // allowed-delay: paces the publisher to a fixed step rate; this is the load
                    // shape under test, not a wait for a condition.
                    await Task.Delay(tick);
            }
            var published = sw.Elapsed;

            var delivered = true;
            foreach (var group in subscribers)
                delivered &= await group.WaitUntilAsync(
                    received => received.Any(e => e.Position == StepsPerRun),
                    Deadline
                );
            var drained = sw.Elapsed - published;

            var outcomes = subscribers
                .SelectMany(g => g.Streams())
                .Select(s =>
                {
                    var numbers = s.BeforeReset.Concat(s.Received).Select(e => e.Sequence).ToList();
                    return (
                        Received: s.Received.Count,
                        Gap: numbers.Zip(numbers.Skip(1), (a, b) => b != a + 1).Any(x => x)
                    );
                })
                .ToArray();
            TestContext.Out.WriteLine(
                $"onJunctionEvent: {Runs * StepsPerRun:N0} steps of {Runs} runs over "
                    + $"{published.TotalMilliseconds:F0}ms to {outcomes.Length:N0} subscribers "
                    + $"({outcomes.Length / Runs} per run); slowest publish on the run's path "
                    + $"{slowest.TotalMilliseconds:F0}ms; drained {drained.TotalMilliseconds:F0}ms "
                    + $"after the last publish; {outcomes.Count(o => o.Received == StepsPerRun):N0} "
                    + $"received their run in full, {outcomes.Count(o => o.Gap):N0} saw a gap"
            );

            delivered.Should().BeTrue("each run's last step should reach its subscribers");
            drained.Should().BeLessThan(TimeSpan.FromSeconds(1));
            slowest
                .Should()
                .BeLessThan(
                    TimeSpan.FromSeconds(1),
                    "a step's publish is bounded, so fan-out never holds a run up, per "
                        + "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md"
                );
            outcomes
                .Should()
                .OnlyContain(
                    o => o.Received == StepsPerRun || o.Gap,
                    "a subscriber that lost steps of its run must see a skip in sequence"
                );
        }
        finally
        {
            foreach (var group in subscribers)
                await group.DisposeAsync();
        }
    }

    /// <summary>One junction event as a subscriber received it.</summary>
    private sealed record Step(long Position, long Sequence);

    private static Task HandleStep(IJunctionEventHandler handler, long metadataId, int position) =>
        handler.HandleAsync(
            new TrainLifecycleEventMessage(
                MetadataId: metadataId,
                ExternalId: metadataId.ToString("D32"),
                TrainName: typeof(IStressProbeTrain).FullName!,
                TrainState: nameof(TrainState.InProgress),
                Timestamp: DateTime.UtcNow,
                FailureJunction: null,
                FailureReason: null,
                EventType: TrainLifecycleEventMessage.JunctionStartedEventType,
                Executor: null,
                Output: null
            )
            {
                Junction = new JunctionEventPayload(
                    position,
                    JunctionRunKind.Junction,
                    "Step" + position,
                    JunctionRunState.InProgress,
                    DateTime.UtcNow
                ),
            },
            CancellationToken.None
        );

    /// <summary>One lifecycle event as a subscriber received it.</summary>
    private sealed record Lifecycle(long MetadataId, long Sequence);

    private Task<Subscribers<Lifecycle>> AttachLifecycleSubscribersAsync() =>
        Subscribers<Lifecycle>.AttachAsync(
            _executor,
            "subscription { onTrainStateChanged { sequence metadataId trainName trainState } }",
            SubscriberCount,
            json => new Lifecycle(
                long.Parse(NumberField(json, "metadataId")),
                long.Parse(NumberField(json, "sequence"))
            ),
            prime: () => Publish(0)
        );

    // Through the publisher the lifecycle hooks use, which numbers each event on its topic.
    private Task Publish(long metadataId) =>
        LifecycleEventPublisher
            .For(_provider.GetRequiredService<ITopicEventSender>())
            .PublishAsync(
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
                ),
                CancellationToken.None
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
    /// received since the last <see cref="Reset"/>, in the order it arrived.
    /// </summary>
    private sealed class Subscribers<T> : IAsyncDisposable
        where T : notnull
    {
        private readonly List<IResponseStream> _streams = [];
        private readonly List<Task> _readers = [];
        private readonly ConcurrentDictionary<int, ConcurrentQueue<T>> _received = new();
        private readonly ConcurrentDictionary<int, T[]> _beforeReset = new();

        // Disposing a response stream ends its topic subscription and so its reader; the readers
        // are also cancelled, so teardown never waits on one.
        private readonly CancellationTokenSource _stop = new();

        public static async Task<Subscribers<T>> AttachAsync(
            IRequestExecutor executor,
            string subscription,
            int count,
            Func<string, T> read,
            Func<Task> prime
        )
        {
            var subscribers = new Subscribers<T>();
            var stop = subscribers._stop.Token;
            for (var i = 0; i < count; i++)
            {
                var result = await executor.ExecuteAsync(subscription);
                var stream = result.ExpectResponseStream();
                var index = i;
                subscribers._received[index] = new ConcurrentQueue<T>();
                subscribers._streams.Add(stream);
                subscribers._readers.Add(
                    Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (
                                var item in stream.ReadResultsAsync().WithCancellation(stop)
                            )
                            {
                                // Serializing each event per subscriber is the work a socket
                                // does too; the field is then read without a second parse, so
                                // the readers are not the bottleneck being measured.
                                subscribers._received[index].Enqueue(read(item.ToJson()));
                            }
                        }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    })
                );
            }

            // Publish primers until every subscriber has one, then start clean.
            var deadline = Stopwatch.StartNew();
            while (subscribers._received.Values.Any(q => q.IsEmpty) && deadline.Elapsed < Deadline)
            {
                await prime();
                // allowed-delay: poll interval while subscriptions register, bounded by Deadline.
                await Task.Delay(50);
            }
            subscribers
                ._received.Values.Should()
                .OnlyContain(q => !q.IsEmpty, "every subscriber must be live before measuring");
            subscribers.Reset();
            return subscribers;
        }

        /// <summary>
        /// Starts every subscriber's received list afresh, remembering the last item each had, so
        /// a measurement can tell what came immediately before it.
        /// </summary>
        public void Reset()
        {
            foreach (var key in _received.Keys)
            {
                var previous = _received[key];
                _received[key] = new ConcurrentQueue<T>();
                _beforeReset[key] = previous.IsEmpty ? [] : [previous.Last()];
            }
        }

        public int[] EventCounts() => _received.Values.Select(q => q.Count).ToArray();

        /// <summary>
        /// For each subscriber, the item it had last before the reset (none, or one), and what it
        /// has received since.
        /// </summary>
        public IEnumerable<(IReadOnlyList<T> BeforeReset, IReadOnlyList<T> Received)> Streams() =>
            _received.Select(kv =>
                (
                    (IReadOnlyList<T>)_beforeReset.GetValueOrDefault(kv.Key, []),
                    (IReadOnlyList<T>)kv.Value.ToArray()
                )
            );

        public async Task<bool> WaitUntilAsync(
            Func<IReadOnlyCollection<T>, bool> done,
            TimeSpan timeout
        )
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (_received.Values.All(q => done(q)))
                    return true;
                // allowed-delay: completion poll, bounded by the caller's timeout.
                await Task.Delay(10);
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            foreach (var stream in _streams)
                await stream.DisposeAsync();
            await Task.WhenAll(_readers);
            _stop.Dispose();
        }
    }
}
