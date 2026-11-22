using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
using Trax.Core.Extensions;
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
/// (<c>onTrainStateChanged</c>), fed by every run's state changes, whole or followed one run at a
/// time by <c>externalId</c>, and <c>onJunctionEvent</c>, fed by every run's steps on one shared
/// topic.
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
            long.TryParse(
                Environment.GetEnvironmentVariable("TRAX_STRESS_SUBSCRIBERS"),
                System.Globalization.CultureInfo.InvariantCulture,
                out var n
            )
                ? n
                : 1_000
        );

    /// <summary>Upper bound on waiting for subscribers to attach or for delivery to finish.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    private ServiceProvider? _provider;
    private IRequestExecutor? _executor;
    private ChangeSignalCoalescer? _coalescer;

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
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress;"
                                ) + "Username=trax;Password=trax123"
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
            .NotNull()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        // A plain ServiceProvider starts no hosted services, so the coalescer is started here.
        _coalescer = _provider
            .NotNull()
            .GetServices<IHostedService>()
            .OfType<ChangeSignalCoalescer>()
            .Single();
        await _coalescer.NotNull().StartAsync(CancellationToken.None);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _coalescer.NotNull().StopAsync(CancellationToken.None);
        _coalescer?.Dispose();
        if (_provider is not null)
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

        var subscribers = await Subscribers<ChangeDomain>.AttachAsync(
            _executor.NotNull(),
            "subscription { onDataChanged { domain timestamp } }",
            SubscriberCount,
            json => Enum.Parse<ChangeDomain>(Pascal(StringField(json, "domain"))),
            prime: () =>
                _provider
                    .NotNull()
                    .GetRequiredService<ITopicEventSender>()
                    .SendAsync(
                        nameof(LifecycleSubscriptions.OnDataChanged),
                        new DataChangedEvent(
                            ChangeDomain.WorkQueue,
                            TimeProvider.System.GetUtcNow().UtcDateTime
                        )
                    )
                    .AsTask()
        );
        await using var subscribersDisposal = subscribers.ConfigureAwait(false);

        var signal = _provider.NotNull().GetRequiredService<ITraxChangeSignal>();
        var sw = TimeProvider.System.GetTimestamp();
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
        var notified = TimeProvider.System.GetElapsedTime(sw);

        var delivered = await subscribers.WaitUntilAsync(
            received => domains.All(received.Contains),
            Deadline
        );
        var swTook = TimeProvider.System.GetElapsedTime(sw);

        await ReportAndAssertDataChangedAsync(
            subscribers,
            domains,
            Writers,
            SignalsPerWriter,
            notified,
            swTook,
            delivered
        );
    }

    private async Task ReportAndAssertDataChangedAsync(
        Subscribers<ChangeDomain> subscribers,
        ChangeDomain[] domains,
        int writers,
        int signalsPerWriter,
        TimeSpan notified,
        TimeSpan swTook,
        bool delivered
    )
    {
        var events = subscribers.EventCounts();
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"onDataChanged: {writers * signalsPerWriter:N0} signals notified in "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{notified.TotalMilliseconds:F0}ms, every one of {SubscriberCount:N0} subscribers had "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"all {domains.Length} domains after {swTook.TotalMilliseconds:F0}ms; events "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"per subscriber min {events.Min()} max {events.Max()}; "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{_provider.NotNull().GetRequiredService<TraxChangeSignal>().TotalDropped:N0} signals "
                    )
                    + "dropped at the full channel"
            )
            .ConfigureAwait(false);

        delivered.Should().BeTrue("every subscriber should hear about every changed domain");
        notified
            .Should()
            .BeLessThan(
                TimeSpan.FromSeconds(1),
                "Notify must never block a write path, however full the channel is"
            );
        swTook.Should().BeLessThan(TimeSpan.FromSeconds(3));
        events
            .Max()
            .Should()
            .BeLessThanOrEqualTo(
                domains.Length * 8,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{writers * signalsPerWriter:N0} signals coalesce into a few events per domain, "
                ) + "not one per write"
            );
    }

    [Test]
    public async Task OnTrainStateChanged_SustainedRuns_ReachEverySubscriberInFullOrSignalAGap()
    {
        // A busy host: 500 state changes a second (250 short runs, each publishing on start and
        // on completion), 2,000 in all, published the way the hooks publish them. The pacing
        // follows the wall clock, so time spent publishing counts toward each tick rather than
        // being added to it; on a loaded machine the publisher may still fall short, and the rate
        // it reached is reported. The feed is lossy by design: a
        // subscriber that falls behind loses its oldest buffered events. What must hold is that it
        // is never silent about it: every subscriber either receives every event, or sees a skip
        // in `sequence` and knows to refetch.
        const int Events = 2_000;
        const int PerTick = 50;
        const int TargetPerSecond = 500;

        var subscribers = await AttachLifecycleSubscribersAsync();
        await using var subscribersDisposal = subscribers.ConfigureAwait(false);

        var sw = TimeProvider.System.GetTimestamp();
        for (var i = 1; i <= Events; i++)
        {
            await Publish(i);
            if (i % PerTick == 0)
                await PaceAsync(sw, i, TargetPerSecond);
        }
        var published = TimeProvider.System.GetElapsedTime(sw);

        var delivered = await subscribers.WaitUntilAsync(
            received => received.Any(e => e.MetadataId == Events),
            Deadline
        );
        var drained = TimeProvider.System.GetElapsedTime(sw) - published;

        await ReportAndAssertSustainedAsync(
            subscribers,
            Events,
            TargetPerSecond,
            published,
            drained,
            delivered
        );
    }

    private static async Task ReportAndAssertSustainedAsync(
        Subscribers<Lifecycle> subscribers,
        int events,
        int targetPerSecond,
        TimeSpan published,
        TimeSpan drained,
        bool delivered
    )
    {
        var outcomes = subscribers.Streams().Select(Outcome).ToArray();
        var complete = outcomes.Count(o => o.Received == events);
        var gapped = outcomes.Count(o => o.Gap);
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"onTrainStateChanged (sustained): {events:N0} events over "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{published.TotalMilliseconds:F0}ms ({events / published.TotalSeconds:F0}/s "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"against a ceiling of {targetPerSecond}/s) reached all {SubscriberCount:N0} subscribers "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{drained.TotalMilliseconds:F0}ms after the last publish; {complete:N0} received "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"all of them, {gapped:N0} saw a gap in sequence; events per subscriber min "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{outcomes.Min(o => o.Received)} max {outcomes.Max(o => o.Received)}"
                    )
            )
            .ConfigureAwait(false);

        delivered.Should().BeTrue("the last run's state change should reach every subscriber");
        drained.Should().BeLessThan(TimeSpan.FromSeconds(1));
        outcomes
            .Should()
            .OnlyContain(
                o => o.Received == events || o.Gap,
                "a subscriber that lost state changes must see a skip in sequence, so it knows to "
                    + "refetch, per docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md"
            );
        outcomes
            .Should()
            .OnlyContain(
                o => !o.Gap || o.Received < events,
                "a subscriber that received every event saw no loss, so it must see no skip"
            );
    }

    [Test]
    public async Task OnTrainStateChanged_FollowingOneRunByExternalId_EachSubscriberGetsOnlyItsRunInFullOrSignalsAGap()
    {
        // The queue-then-follow pattern at scale: many runs changing state at once on the one
        // lifecycle topic, each followed by its own share of the subscribers through
        // `externalId`. Every subscriber reads every run's events and keeps only its run's, as on
        // the junction topic, so what is measured is the fan-out with the filter applied. Paced to
        // at most 500 state changes a second.
        const int Runs = 100;
        const int ChangesPerRun = 20;
        const int PerTick = 50;
        const int TargetPerSecond = 500;

        var subscribers = new List<Subscribers<RunChange>>();
        try
        {
            await AttachRunFollowersAsync(subscribers, Runs);

            var sw = TimeProvider.System.GetTimestamp();
            var sent = 0;
            for (var change = 1; change <= ChangesPerRun; change++)
            for (var run = 1; run <= Runs; run++)
            {
                await Publish(
                    run,
                    change == ChangesPerRun ? TrainState.Completed : TrainState.InProgress
                );
                if (++sent % PerTick == 0)
                    await PaceAsync(sw, sent, TargetPerSecond);
            }
            var published = TimeProvider.System.GetElapsedTime(sw);

            var delivered = true;
            foreach (var group in subscribers)
                delivered &= await group.WaitUntilAsync(
                    received => received.Any(e => e.Last),
                    Deadline
                );
            var drained = TimeProvider.System.GetElapsedTime(sw) - published;

            var outcomes = RunFollowerOutcomes(subscribers);
            await ReportAndAssertRunFollowersAsync(
                Runs,
                ChangesPerRun,
                TargetPerSecond,
                outcomes,
                published,
                drained,
                delivered
            );
        }
        finally
        {
            foreach (var group in subscribers)
                await group.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Attaches each run's share of the subscribers, following it by <c>externalId</c>.</summary>
    private async Task AttachRunFollowersAsync(List<Subscribers<RunChange>> subscribers, int runs)
    {
        for (var run = 1; run <= runs; run++)
        {
            var metadataId = run;
            subscribers.Add(
                await Subscribers<RunChange>
                    .AttachAsync(
                        _executor.NotNull(),
                        "subscription { onTrainStateChanged(externalId: \""
                            + metadataId.ToString(
                                "D32",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                            + "\") { sequence metadataId trainState } }",
                        Math.Max(1, SubscriberCount / runs),
                        json => new RunChange(
                            long.Parse(
                                NumberField(json, "metadataId"),
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            long.Parse(
                                NumberField(json, "sequence"),
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            string.Equals(
                                StringField(json, "trainState"),
                                "COMPLETED",
                                StringComparison.Ordinal
                            )
                        ),
                        prime: () => Publish(metadataId, TrainState.InProgress)
                    )
                    .ConfigureAwait(false)
            );
        }
    }

    /// <summary>
    /// How many changes each subscriber received, how many of them belong to another run, and
    /// whether its sequence skipped.
    /// </summary>
    private static (int Received, int Foreign, bool Gap)[] RunFollowerOutcomes(
        List<Subscribers<RunChange>> subscribers
    ) =>
        subscribers
            .SelectMany((g, index) => g.Streams().Select(s => (Run: index + 1L, s)))
            .Select(x =>
            {
                var numbers = x.s.BeforeReset.Concat(x.s.Received).Select(e => e.Sequence).ToList();
                return (
                    Received: x.s.Received.Count,
                    Foreign: x.s.Received.Count(e => e.MetadataId != x.Run),
                    Gap: numbers.Zip(numbers.Skip(1), (a, b) => b != a + 1).Any(g => g)
                );
            })
            .ToArray();

    private static async Task ReportAndAssertRunFollowersAsync(
        int runs,
        int changesPerRun,
        int targetPerSecond,
        (int Received, int Foreign, bool Gap)[] outcomes,
        TimeSpan published,
        TimeSpan drained,
        bool delivered
    )
    {
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"onTrainStateChanged(externalId): {runs * changesPerRun:N0} changes of {runs} runs "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"over {published.TotalMilliseconds:F0}ms "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"({runs * changesPerRun / published.TotalSeconds:F0}/s against a ceiling of "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{targetPerSecond}/s) to {outcomes.Length:N0} subscribers "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"({outcomes.Length / runs} per run); drained {drained.TotalMilliseconds:F0}ms "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"after the last publish; {outcomes.Count(o => o.Received == changesPerRun):N0} "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"received their run in full, {outcomes.Count(o => o.Gap):N0} saw a gap"
                    )
            )
            .ConfigureAwait(false);

        delivered.Should().BeTrue("each run's last state change should reach its followers");
        drained.Should().BeLessThan(TimeSpan.FromSeconds(1));
        outcomes
            .Should()
            .OnlyContain(o => o.Foreign == 0, "a subscriber following one run sees only it");
        outcomes
            .Should()
            .OnlyContain(
                o => o.Received == changesPerRun || o.Gap,
                "a subscriber that lost changes of its run must see a skip in sequence, per "
                    + "docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md"
            );
    }

    /// <summary>One state change of a followed run as its subscriber received it.</summary>
    private sealed record RunChange(long MetadataId, long Sequence, bool Last);

    /// <summary>
    /// Holds the publisher back until <paramref name="sent"/> events are due at
    /// <paramref name="perSecond"/>. A publisher already behind that schedule is not delayed, so
    /// the rate is a ceiling and the time spent publishing is never added to on top of it.
    /// </summary>
    private static async Task PaceAsync(long started, int sent, int perSecond)
    {
        var due = TimeSpan.FromSeconds((double)sent / perSecond);
        var ahead = due - TimeProvider.System.GetElapsedTime(started);
        if (ahead > TimeSpan.Zero)
            // allowed-delay: paces the publisher to a ceiling rate; this is the load shape under
            // test, not a wait for a condition.
            await Task.Delay(ahead, TimeProvider.System).ConfigureAwait(false);
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

        var subscribers = await AttachLifecycleSubscribersAsync();
        await using var subscribersDisposal = subscribers.ConfigureAwait(false);

        var sw = TimeProvider.System.GetTimestamp();
        for (var i = 1; i <= Events; i++)
            await Publish(i);
        var published = TimeProvider.System.GetElapsedTime(sw);

        var delivered = await subscribers.WaitUntilAsync(
            received => received.Any(e => e.MetadataId == Events),
            Deadline
        );
        var swTook = TimeProvider.System.GetElapsedTime(sw);

        var events = subscribers.EventCounts();
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"onTrainStateChanged (burst): {Events:N0} events published in "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{published.TotalMilliseconds:F0}ms, the last one reached all {SubscriberCount:N0} "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"subscribers after {swTook.TotalMilliseconds:F0}ms; events kept per "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"subscriber min {events.Min()} max {events.Max()}"
                    )
            )
            .ConfigureAwait(false);

        delivered.Should().BeTrue("the last run's state change should reach every subscriber");
        swTook.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task OnJunctionEvent_ManyRunsOnTheTopic_EachSubscriberGetsItsRunInFullOrSignalsAGap()
    {
        // Many runs stepping at once on the one junction topic, each followed by its own share of
        // the subscribers: every subscriber reads every run's steps and keeps only its run's, so
        // the fan-out cost is subscribers x all steps, not subscribers x their run's steps. The
        // steps go through the handler the run's path calls, so its publish bound is what is
        // measured on the run's side. Paced to at most 500 steps a second; the rate reached is
        // reported, since the publisher's own fan-out may hold it below that.
        const int Runs = 100;
        const int StepsPerRun = 20;
        const int PerTick = 50;
        const int TargetPerSecond = 500;

        var handler = _provider.NotNull().GetServices<IJunctionEventHandler>().Single();
        var subscribers = new List<Subscribers<Step>>();
        try
        {
            await AttachStepFollowersAsync(subscribers, handler, Runs);

            var slowest = TimeSpan.Zero;
            var sw = TimeProvider.System.GetTimestamp();
            var sent = 0;
            for (var position = 1; position <= StepsPerRun; position++)
            for (var run = 1; run <= Runs; run++)
            {
                var publish = TimeProvider.System.GetTimestamp();
                await HandleStep(handler, run, position);
                if (TimeProvider.System.GetElapsedTime(publish) > slowest)
                    slowest = TimeProvider.System.GetElapsedTime(publish);
                if (++sent % PerTick == 0)
                    await PaceAsync(sw, sent, TargetPerSecond);
            }
            var published = TimeProvider.System.GetElapsedTime(sw);

            var delivered = true;
            foreach (var group in subscribers)
                delivered &= await group.WaitUntilAsync(
                    received => received.Any(e => e.Position == StepsPerRun),
                    Deadline
                );
            var drained = TimeProvider.System.GetElapsedTime(sw) - published;

            var outcomes = StepFollowerOutcomes(subscribers);
            await ReportAndAssertStepFollowersAsync(
                Runs,
                StepsPerRun,
                TargetPerSecond,
                outcomes,
                published,
                slowest,
                drained,
                delivered
            );
        }
        finally
        {
            foreach (var group in subscribers)
                await group.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Attaches each run's share of the subscribers to its steps on the junction topic.</summary>
    private async Task AttachStepFollowersAsync(
        List<Subscribers<Step>> subscribers,
        IJunctionEventHandler handler,
        int runs
    )
    {
        for (var run = 1; run <= runs; run++)
        {
            var metadataId = run;
            subscribers.Add(
                await Subscribers<Step>
                    .AttachAsync(
                        _executor.NotNull(),
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"subscription {{ onJunctionEvent(metadataId: {metadataId}) "
                        ) + "{ sequence metadataId junction { position } } }",
                        Math.Max(1, SubscriberCount / runs),
                        json => new Step(
                            long.Parse(
                                NumberField(json, "position"),
                                System.Globalization.CultureInfo.InvariantCulture
                            ),
                            long.Parse(
                                NumberField(json, "sequence"),
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        ),
                        prime: () => HandleStep(handler, metadataId, position: 0)
                    )
                    .ConfigureAwait(false)
            );
        }
    }

    /// <summary>How many steps each subscriber received, and whether its sequence skipped.</summary>
    private static (int Received, bool Gap)[] StepFollowerOutcomes(
        List<Subscribers<Step>> subscribers
    ) =>
        subscribers
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

    private static async Task ReportAndAssertStepFollowersAsync(
        int runs,
        int stepsPerRun,
        int targetPerSecond,
        (int Received, bool Gap)[] outcomes,
        TimeSpan published,
        TimeSpan slowest,
        TimeSpan drained,
        bool delivered
    )
    {
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"onJunctionEvent: {runs * stepsPerRun:N0} steps of {runs} runs over "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{published.TotalMilliseconds:F0}ms "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"({runs * stepsPerRun / published.TotalSeconds:F0}/s against a ceiling of "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{targetPerSecond}/s) to {outcomes.Length:N0} subscribers "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"({outcomes.Length / runs} per run); slowest publish on the run's path "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{slowest.TotalMilliseconds:F0}ms; drained {drained.TotalMilliseconds:F0}ms "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"after the last publish; {outcomes.Count(o => o.Received == stepsPerRun):N0} "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"received their run in full, {outcomes.Count(o => o.Gap):N0} saw a gap"
                    )
            )
            .ConfigureAwait(false);

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
                o => o.Received == stepsPerRun || o.Gap,
                "a subscriber that lost steps of its run must see a skip in sequence"
            );
    }

    /// <summary>One junction event as a subscriber received it.</summary>
    private sealed record Step(long Position, long Sequence);

    private static Task HandleStep(IJunctionEventHandler handler, long metadataId, int position) =>
        handler.HandleAsync(
            new TrainLifecycleEventMessage(
                MetadataId: metadataId,
                ExternalId: metadataId.ToString(
                    "D32",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                TrainName: typeof(IStressProbeTrain).FullName.NotNull(),
                TrainState: nameof(TrainState.InProgress),
                Timestamp: TimeProvider.System.GetUtcNow().UtcDateTime,
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
                    "Step" + position.ToString(CultureInfo.InvariantCulture),
                    JunctionRunState.InProgress,
                    TimeProvider.System.GetUtcNow().UtcDateTime
                ),
            },
            CancellationToken.None
        );

    /// <summary>One lifecycle event as a subscriber received it.</summary>
    private sealed record Lifecycle(long MetadataId, long Sequence);

    private Task<Subscribers<Lifecycle>> AttachLifecycleSubscribersAsync() =>
        Subscribers<Lifecycle>.AttachAsync(
            _executor.NotNull(),
            "subscription { onTrainStateChanged { sequence metadataId trainName trainState } }",
            SubscriberCount,
            json => new Lifecycle(
                long.Parse(
                    NumberField(json, "metadataId"),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                long.Parse(
                    NumberField(json, "sequence"),
                    System.Globalization.CultureInfo.InvariantCulture
                )
            ),
            prime: () => Publish(0)
        );

    // Through the publisher the lifecycle hooks use, which numbers each event on its topic.
    private Task Publish(long metadataId, TrainState state = TrainState.Completed) =>
        LifecycleEventPublisher
            .For(_provider.NotNull().GetRequiredService<ITopicEventSender>())
            .PublishAsync(
                nameof(LifecycleSubscriptions.OnTrainStateChanged),
                new TrainLifecycleEvent(
                    metadataId,
                    metadataId.ToString("D32", System.Globalization.CultureInfo.InvariantCulture),
                    typeof(IStressProbeTrain).FullName.NotNull(),
                    state,
                    TimeProvider.System.GetUtcNow().UtcDateTime,
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
                var result = await executor
                    .ExecuteAsync(subscription, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                var stream = result.ExpectResponseStream();
                var index = i;
                subscribers._received[index] = new ConcurrentQueue<T>();
                subscribers._streams.Add(stream);
                subscribers._readers.Add(
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                await foreach (
                                    var item in stream
                                        .ReadResultsAsync()
                                        .WithCancellation(stop)
                                        .ConfigureAwait(false)
                                )
                                {
                                    // Serializing each event per subscriber is the work a socket
                                    // does too; the field is then read without a second parse, so
                                    // the readers are not the bottleneck being measured.
                                    subscribers._received[index].Enqueue(read(item.ToJson()));
                                }
                            }
                            catch (OperationCanceledException) when (stop.IsCancellationRequested)
                            { }
                        },
                        cancellationToken: CancellationToken.None
                    )
                );
            }

            // Publish primers until every subscriber has one, then start clean.
            var deadline = TimeProvider.System.GetTimestamp();
            while (
                subscribers._received.Values.Any(q => q.IsEmpty)
                && TimeProvider.System.GetElapsedTime(deadline) < Deadline
            )
            {
                await prime().ConfigureAwait(false);
                // allowed-delay: poll interval while subscriptions register, bounded by Deadline.
                await Task.Delay(50, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
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
            var sw = TimeProvider.System.GetTimestamp();
            while (TimeProvider.System.GetElapsedTime(sw) < timeout)
            {
                if (_received.Values.All(q => done(q)))
                    return true;
                // allowed-delay: completion poll, bounded by the caller's timeout.
                await Task.Delay(10, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            foreach (var stream in _streams)
                await stream.DisposeAsync().ConfigureAwait(false);
            await Task.WhenAll(_readers).ConfigureAwait(false);
            _stop.Dispose();
        }
    }
}
