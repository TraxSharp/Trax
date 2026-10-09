using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// With <c>AddDecisionRecording</c>, every decision a train makes is written against its run as
/// it is made, and a run that names an earlier one in <c>ReplayDecisionsOf</c> takes the tracks
/// that run took instead of asking again.
///
/// <para>Enforces docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md")]
public class DecisionRecordingTests
{
    private const string Adr = "docs/adr/0020-a-recorded-answer-replays-only-while-it-is-fresh.md";

    private ServiceProvider _provider = null!;

    private static readonly DeciderSlot Decider = new();

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [OneTimeSetUp]
    public void Build() =>
        _provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddDecisionRecording().AddDecisionRecording()
                )
            )
            .BuildServiceProvider();

    [OneTimeTearDown]
    public async Task Dispose() => await _provider.DisposeAsync();

    [Test]
    public async Task A_run_records_each_decision_and_the_track_it_took()
    {
        var decider = Decider.Use(
            new ScriptedDecider().Choose(Fulfilment.Standard, 0.97, model: "jev-1.13.0")
        );

        var (train, output) = await Run<IRouteOrder>(new Order("o1", 20m));

        output.Should().Be("shipped");
        var recorded = await Recorded(train.Metadata!.Id);
        var decision = recorded.Should().ContainSingle().Subject;
        decision.QuestionKey.Should().Be(QuestionKey.For<Fulfilment>());
        decision.QuestionKey.Should().Be("Fulfilment", "the key leaves the namespace out");
        decision.Occurrence.Should().Be(0);
        decision.Kind.Should().Be("choice");
        decision.Model.Should().Be("jev-1.13.0");
        decision.Decider.Should().Be(typeof(DeciderSlot).FullName);
        decision.Replayed.Should().BeFalse();
        decision.Tracks().Should().Equal("Standard");
        decision.Routes.Should().Contain("\"fallback_reason\":null");
        decision.Answer.Should().Contain("\"choice\":\"Standard\"");
        decision.Question.Should().Contain("How should this order be fulfilled?");
        decider.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task A_decision_that_was_not_followed_records_why()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard, 0.4));

        var (train, output) = await Run<IRouteOrder>(new Order("o2", 20m));

        output.Should().Be("held for review");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.Tracks().Should().Equal("Otherwise");
        decision.Routes.Should().Contain("confidence of 0.4");
    }

    [Test]
    public async Task Every_track_taken_on_one_decision_is_recorded()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard, 0.9));

        var (train, output) = await Run<IDecideThenSwitchTwice>(new Order("o-two-routes", 20m));

        output.Should().Be("held for review", "the second switch wants more confidence");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.Tracks().Should().Equal("Standard", "Otherwise");
        decision.Routes.Should().Contain("confidence of 0.9");
    }

    [Test]
    public async Task A_decision_is_written_before_the_run_ends()
    {
        // What a run killed mid-way (an OOM, a timeout, a deploy) leaves behind is whatever was
        // written before it died. The junction after the switch reads the table through a
        // context of its own while the run is still in progress.
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));

        var (train, _) = await Run<IRouteThenPeek>(new Order("o-durable", 20m));

        var seen = PeekDecisions.Seen.Should().ContainSingle().Subject;
        seen.MetadataId.Should().Be(train.Metadata!.Id);
        seen.State.Should().Be(TrainState.InProgress);
        seen.Track.Should().Be("ManualCheck");
    }

    [Test]
    public async Task A_run_that_fails_after_deciding_still_records_the_decision()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));

        using var scope = _provider.CreateScope();
        var train = (RouteThenFail)scope.ServiceProvider.GetRequiredService<IRouteThenFail>();

        var run = () => train.Run(new Order("o3", 9000m));

        await run.Should().ThrowAsync<InvalidOperationException>();
        (await Recorded(train.Metadata!.Id))
            .Should()
            .ContainSingle()
            .Which.Tracks()
            .Should()
            .Equal("ManualCheck");
    }

    [Test]
    public async Task A_requeued_run_replays_the_original_decisions_without_asking()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, firstOutput) = await Run<IRouteOrder>(new Order("o4", 20m));
        firstOutput.Should().Be("held for review");

        // Asked now, the decider would ship it. The requeue must not ask.
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o4", 20m),
            replayDecisionsOf: original.Metadata!.Id
        );

        output.Should().Be("held for review", "the requeue takes the track the original took");
        decider.Requests.Should().BeEmpty();
        var replayed = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        replayed.Replayed.Should().BeTrue();
        replayed.Decider.Should().BeNull();
        replayed.Tracks().Should().Equal("ManualCheck");
    }

    [Test]
    public async Task A_decision_is_recorded_with_the_hash_of_its_state()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (first, _) = await Run<IRouteOrder>(new Order("o-hash", 20m));
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (other, _) = await Run<IRouteOrder>(new Order("o-hash", 2000m));

        var one = (await Recorded(first.Metadata!.Id)).Should().ContainSingle().Subject;
        var two = (await Recorded(other.Metadata!.Id)).Should().ContainSingle().Subject;

        one.StateHash.Should().MatchRegex("^s1:[0-9a-f]{64}$");
        two.StateHash.Should().NotBe(one.StateHash, $"the states differ. See {Adr}.");
    }

    [Test]
    public async Task A_requeue_whose_state_changed_since_asks_afresh()
    {
        StockLevel.Total = 20m;
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (original, first) = await Run<IReadThenRoute>(new Order("o-moved", 0m));
        first.Should().Be("shipped");

        // What the junction before the decision reads has changed while the run waited.
        StockLevel.Total = 2000m;
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));

        var (requeued, output) = await Run<IReadThenRoute>(
            new Order("o-moved", 0m),
            replayDecisionsOf: original.Metadata!.Id
        );

        output
            .Should()
            .Be(
                "held for review",
                $"the decider was asked about the state as it is now. See {Adr}."
            );
        decider.Requests.Should().ContainSingle();
        var asked = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        asked.Replayed.Should().BeFalse();
        asked.Answer.Should().Contain("replay_refused");
    }

    [Test]
    public async Task A_requeue_whose_state_is_unchanged_replays()
    {
        StockLevel.Total = 20m;
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IReadThenRoute>(new Order("o-same", 0m));
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IReadThenRoute>(
            new Order("o-same", 0m),
            replayDecisionsOf: original.Metadata!.Id
        );

        output.Should().Be("held for review");
        decider.Requests.Should().BeEmpty();
        (await Recorded(requeued.Metadata!.Id))
            .Should()
            .ContainSingle()
            .Which.Replayed.Should()
            .BeTrue();
    }

    [Test]
    public async Task An_answer_recorded_without_a_state_hash_is_asked_afresh()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-old-row", 20m));

        // A row written before the column existed.
        await Rewrite(original.Metadata!.Id, d => d.StateHash = null);
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-old-row", 20m),
            replayDecisionsOf: original.Metadata.Id
        );

        output.Should().Be("shipped");
        decider.Requests.Should().ContainSingle();
        (await Recorded(requeued.Metadata!.Id)).Single().Replayed.Should().BeFalse();
    }

    [Test]
    public async Task An_answer_older_than_the_replay_bound_is_asked_afresh()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-aged", 20m));
        await Rewrite(
            original.Metadata!.Id,
            d =>
                d.DecidedAt =
                    DateTime.UtcNow
                    - DecisionRecordingOptions.DefaultMaxReplayAge
                    - TimeSpan.FromMinutes(1)
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-aged", 20m),
            replayDecisionsOf: original.Metadata.Id
        );

        output
            .Should()
            .Be("shipped", $"an answer older than the bound is not replayed. See {Adr}.");
        decider.Requests.Should().ContainSingle();
        (await Recorded(requeued.Metadata!.Id)).Single().Replayed.Should().BeFalse();
    }

    [Test]
    public async Task A_replayed_answer_ages_from_when_a_decider_gave_it()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-chain", 20m));
        var (middle, _) = await Run<IRouteOrder>(
            new Order("o-chain", 20m),
            replayDecisionsOf: original.Metadata!.Id
        );
        (await Recorded(middle.Metadata!.Id)).Single().Replayed.Should().BeTrue();

        // The original answer is now old; the middle run's replay of it is recent.
        await Rewrite(
            original.Metadata.Id,
            d =>
                d.DecidedAt =
                    DateTime.UtcNow
                    - DecisionRecordingOptions.DefaultMaxReplayAge
                    - TimeSpan.FromMinutes(1)
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (last, output) = await Run<IRouteOrder>(
            new Order("o-chain", 20m),
            replayDecisionsOf: middle.Metadata.Id
        );

        output.Should().Be("shipped", $"replaying an answer does not make it younger. See {Adr}.");
        decider.Requests.Should().ContainSingle();
        (await Recorded(last.Metadata!.Id)).Single().Replayed.Should().BeFalse();
    }

    [Test]
    public void The_replay_bound_defaults_to_a_day_and_refuses_less_than_a_second()
    {
        DecisionRecordingOptions.DefaultMaxReplayAge.Should().Be(TimeSpan.FromHours(24));
        var options = (DecisionRecordingOptions)
            Activator.CreateInstance(typeof(DecisionRecordingOptions), nonPublic: true)!;

        options.MaxReplayAge.Should().Be(TimeSpan.FromHours(24));
        options
            .ReplayAnswersFor(TimeSpan.FromHours(2))
            .MaxReplayAge.Should()
            .Be(TimeSpan.FromHours(2));
        var act = () => options.ReplayAnswersFor(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task A_replay_bound_longer_than_the_calendar_replays_answers_of_any_age()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .UseInMemory()
                        .AddDecisionRecording(o => o.ReplayAnswersFor(TimeSpan.MaxValue))
                )
            )
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var original = await RunOn(provider, new Order("o-forever", 20m), replayDecisionsOf: null);
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var output = await RunOn(
            provider,
            new Order("o-forever", 20m),
            replayDecisionsOf: original
        );

        decider.Requests.Should().BeEmpty($"no bound means any age replays. See {Adr}.");
    }

    [Test]
    public async Task A_journal_the_host_registers_itself_replays_for_the_configured_bound()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<DecisionJournal>()
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .UseInMemory()
                        .AddDecisionRecording(o => o.ReplayAnswersFor(TimeSpan.FromMinutes(5)))
                )
            )
            .BuildServiceProvider();

        provider
            .GetRequiredService<DecisionJournal>()
            .Options.MaxReplayAge.Should()
            .Be(
                TimeSpan.FromMinutes(5),
                $"the bound applies however the journal is built. See {Adr}."
            );
    }

    [Test]
    public async Task An_observer_that_cannot_be_built_refuses_with_a_clear_reason()
    {
        var services = DecisionTrains.Register(new ServiceCollection(), Decider);
        services.AddSingleton<IDecisionObserver>(_ =>
            throw new InvalidOperationException("the audit sink is not configured")
        );
        await using var provider = services
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        var gate = provider.GetServices<IHostedService>().OfType<DecisionObserverCheck>().Single();

        for (var i = 0; i < 2; i++)
        {
            var start = () => gate.StartingAsync(CancellationToken.None);
            await start
                .Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*could not be built*the audit sink is not configured*");
        }

        using var scope = provider.CreateScope();
        var train =
            (ServiceTrain<Order, string>)
                (object)scope.ServiceProvider.GetRequiredService<IRouteOrder>();
        var run = async () => await train.Run(new Order("o-unbuilt", 20m));
        await run.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be built*");
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent, $"See {Adr}.");
    }

    [Test]
    public async Task A_manifest_retry_whose_source_run_is_gone_asks_afresh()
    {
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        await RunOn(
            _provider,
            new Order("o-retry-gone", 20m),
            replayDecisionsOf: 987_654_321,
            manifestId: 1
        );

        decider
            .Requests.Should()
            .ContainSingle($"a manifest's retry asks afresh rather than fail. See {Adr}.");
    }

    [Test]
    public async Task A_manifest_retry_on_a_host_that_records_no_decisions_asks_afresh()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider();
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var retry = await RunOn(
            provider,
            new Order("o-retry-unrecorded", 20m),
            replayDecisionsOf: 1,
            manifestId: 1
        );

        decider.Requests.Should().ContainSingle();
        using var scope = provider.CreateScope();
        var row = await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == retry);
        row.ReplayAbandoned.Should().BeTrue($"the retry asked afresh. See {Adr}.");
        row.ReplayDecisionsOf.Should().Be(1, "what it was queued to do is kept");
    }

    [Test]
    public async Task A_manifest_retry_that_asked_afresh_is_replayed_for_its_own_answers()
    {
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var retry = await RunOn(
            _provider,
            new Order("o-retry-abandoned", 20m),
            replayDecisionsOf: 987_654_323,
            manifestId: 1
        );
        decider.Requests.Should().ContainSingle();

        var later = Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-retry-abandoned", 20m),
            replayDecisionsOf: retry
        );

        output.Should().Be("shipped", $"the retry's own answer is the one to repeat. See {Adr}.");
        later.Requests.Should().BeEmpty();
        (await Recorded(requeued.Metadata!.Id)).Single().Replayed.Should().BeTrue();
    }

    [Test]
    public async Task A_requeue_of_a_retry_that_never_started_replays_the_run_before_it()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-never-started", 20m));

        // A manifest's retry whose row was written but which never ran: it abandoned nothing.
        var retry = await Seed<IRouteOrder>(
            replayDecisionsOf: original.Metadata!.Id,
            manifestId: 1
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (_, output) = await Run<IRouteOrder>(
            new Order("o-never-started", 20m),
            replayDecisionsOf: retry
        );

        output.Should().Be("held for review", "the chain passes through a run that never ran");
        decider.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task A_replay_stops_at_a_run_that_abandoned_its_replay()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-stop", 20m));

        // A retry of the original that asked afresh and died before it reached the question.
        var retry = await Seed<IRouteOrder>(
            replayDecisionsOf: original.Metadata!.Id,
            decisionsRecorded: true,
            replayAbandoned: true
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (_, output) = await Run<IRouteOrder>(
            new Order("o-stop", 20m),
            replayDecisionsOf: retry
        );

        output
            .Should()
            .Be(
                "shipped",
                $"the retry never acted on the original's answers, so neither does its replay. See {Adr}."
            );
        decider.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task A_requeue_of_a_retry_that_asked_afresh_unrecorded_fails()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-unrecorded-retry", 20m));

        // A manifest's retry on a host that records nothing, which asked afresh.
        var retry = await Seed<IRouteOrder>(
            replayDecisionsOf: original.Metadata!.Id,
            replayAbandoned: true,
            manifestId: 1
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (_, run) = Start<IRouteOrder>(
            new Order("o-unrecorded-retry", 20m),
            replayDecisionsOf: retry
        );

        await run.Should()
            .ThrowAsync<Exception>()
            .WithMessage($"*run {retry} ran without recording its decisions*");
        decider.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task A_manifest_retry_whose_recorded_answer_is_unreadable_asks_afresh()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var original = await RunOn(
            _provider,
            new Order("o-retry-damaged", 20m),
            replayDecisionsOf: null
        );
        await Rewrite(
            original,
            d => d.Answer = """{"type":"choice","choice":"ManualCheck","confidence":"lots"}"""
        );
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        await RunOn(
            _provider,
            new Order("o-retry-damaged", 20m),
            replayDecisionsOf: original,
            manifestId: 1
        );

        decider.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task A_manual_requeue_whose_source_run_is_gone_still_fails()
    {
        var run = () =>
            RunOn(_provider, new Order("o-requeue-gone", 20m), replayDecisionsOf: 987_654_322);

        await run.Should().ThrowAsync<Exception>().WithMessage("*no run 987654322 exists*");
    }

    private static async Task<long> RunOn(
        IServiceProvider provider,
        Order order,
        long? replayDecisionsOf,
        long? manifestId = null
    )
    {
        using var scope = provider.CreateScope();
        var train =
            (ServiceTrain<Order, string>)
                (object)scope.ServiceProvider.GetRequiredService<IRouteOrder>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRouteOrder).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = order,
                ReplayDecisionsOf = replayDecisionsOf,
                ManifestId = manifestId,
            }
        );
        await train.Run(order, metadata);
        return train.Metadata!.Id;
    }

    [Test]
    public async Task The_replay_bound_is_configurable()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .UseInMemory()
                        .AddDecisionRecording(o => o.ReplayAnswersFor(TimeSpan.FromHours(1)))
                )
            )
            .BuildServiceProvider();

        provider
            .GetRequiredService<DecisionJournal>()
            .Options.MaxReplayAge.Should()
            .Be(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task A_decision_is_recorded_with_the_fingerprint_of_its_asking()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-print", 20m));
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (again, _) = await Run<IRouteOrder>(new Order("o-print-again", 99m));

        var first = (await Recorded(original.Metadata!.Id)).Should().ContainSingle().Subject;
        var second = (await Recorded(again.Metadata!.Id)).Should().ContainSingle().Subject;

        first.Fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        second
            .Fingerprint.Should()
            .Be(first.Fingerprint, "the same step asks the same question; the state never counts");
    }

    [Test]
    public async Task A_recorded_answer_whose_fingerprint_differs_is_not_replayed()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-stale", 20m));

        // As if the question had been reworded since: the stored fingerprint is what comes back.
        await Rewrite(original.Metadata!.Id, d => d.Fingerprint = new string('0', 64));

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-stale", 20m),
            replayDecisionsOf: original.Metadata.Id
        );

        output.Should().Be("shipped", "the decider was asked afresh");
        decider.Requests.Should().ContainSingle();
        var asked = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        asked.Replayed.Should().BeFalse();
        asked.Answer.Should().Contain("replay_refused");
    }

    [Test]
    public async Task Each_asking_of_a_question_is_recorded_and_replayed_by_its_occurrence()
    {
        Decider.Use(new SequenceDecider(Fulfilment.ManualCheck, Fulfilment.Standard));
        var (original, firstOutput) = await Run<IRouteTwice>(new Order("o-twice", 20m));
        firstOutput.Should().Be("shipped");

        var recorded = (await Recorded(original.Metadata!.Id)).OrderBy(d => d.Occurrence).ToList();
        recorded.Select(d => d.Occurrence).Should().Equal(0, 1);
        recorded.Select(d => d.Tracks().Single()).Should().Equal("ManualCheck", "Standard");

        // Asked now, both askings would be answered the other way round.
        var decider = Decider.Use(new SequenceDecider(Fulfilment.Standard, Fulfilment.ManualCheck));

        var (requeued, output) = await Run<IRouteTwice>(
            new Order("o-twice", 20m),
            replayDecisionsOf: original.Metadata.Id
        );

        output.Should().Be("shipped");
        decider.Asked.Should().Be(0);
        (await Recorded(requeued.Metadata!.Id))
            .OrderBy(d => d.Occurrence)
            .Select(d => (d.Occurrence, d.Tracks().Single(), d.Replayed))
            .Should()
            .Equal((0, "ManualCheck", true), (1, "Standard", true));
    }

    [Test]
    public async Task A_replay_of_a_run_that_does_not_exist_fails_the_run_as_permanent()
    {
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (train, run) = Start<IRouteOrder>(new Order("o5", 20m), replayDecisionsOf: 987_654_321);

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("no run 987654321 exists");
        decider.Requests.Should().BeEmpty("the run fails before it asks anything");
        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task A_replay_of_a_run_of_another_train_fails_the_run_as_permanent()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (other, _) = await Run<IRouteWithShadow>(new Order("o-other", 20m));

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (train, run) = Start<IRouteOrder>(
            new Order("o-other", 20m),
            replayDecisionsOf: other.Metadata!.Id
        );

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain(
                $"run {other.Metadata.Id} is a run of train '{typeof(IRouteWithShadow).FullName}'"
            );
        decider.Requests.Should().BeEmpty("the run fails before it asks anything");
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task A_requeue_of_a_requeue_that_recorded_nothing_replays_the_run_before_it()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-chain", 20m));

        // A requeue of the original that failed before it reached the question.
        var failedRequeue = await Seed<IRouteOrder>(replayDecisionsOf: original.Metadata!.Id);

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-chain", 20m),
            replayDecisionsOf: failedRequeue
        );

        output.Should().Be("held for review", "the original's answer is still the one to repeat");
        decider.Requests.Should().BeEmpty();
        (await Recorded(requeued.Metadata!.Id))
            .Should()
            .ContainSingle()
            .Which.Replayed.Should()
            .BeTrue();
    }

    [Test]
    public async Task A_replay_takes_the_nearer_runs_answer_and_falls_back_to_the_older_one()
    {
        Decider.Use(new SequenceDecider(Fulfilment.ManualCheck, Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteTwice>(new Order("o-layers", 20m));

        // The first requeue acted on a different first answer (as one asked afresh after the
        // original's stopped fitting would), then died before its second asking.
        Decider.Use(new SequenceDecider());
        var (firstRequeue, _) = await Run<IRouteTwice>(
            new Order("o-layers", 20m),
            replayDecisionsOf: original.Metadata!.Id
        );
        await Rewrite(
            firstRequeue.Metadata!.Id,
            d => d.Answer = d.Answer?.Replace("ManualCheck", "Standard")
        );
        await Forget(firstRequeue.Metadata.Id, occurrence: 1);

        var decider = Decider.Use(new SequenceDecider(Fulfilment.Standard, Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteTwice>(
            new Order("o-layers", 20m),
            replayDecisionsOf: firstRequeue.Metadata.Id
        );

        output.Should().Be("held for review", "the second asking repeats the original's answer");
        decider.Asked.Should().Be(0);
        (await Recorded(requeued.Metadata!.Id))
            .OrderBy(d => d.Occurrence)
            .Select(d => (d.Occurrence, d.Tracks().Single(), d.Replayed))
            .Should()
            .Equal((0, "Standard", true), (1, "ManualCheck", true));
    }

    [Test]
    public async Task A_replay_whose_runs_lead_back_on_themselves_fails_the_run_as_permanent()
    {
        var first = await Seed<IRouteOrder>(replayDecisionsOf: null);
        var second = await Seed<IRouteOrder>(replayDecisionsOf: first);
        await Relink(first, second);

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (train, run) = Start<IRouteOrder>(new Order("o-loop", 20m), replayDecisionsOf: second);

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain($"lead back to run {second}");
        decider.Requests.Should().BeEmpty();
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task A_replay_that_goes_back_too_many_runs_fails_the_run_as_permanent()
    {
        long? previous = null;

        for (var i = 0; i <= DecisionJournal.MaxReplayChain; i++)
            previous = await Seed<IRouteOrder>(replayDecisionsOf: previous);

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (train, run) = Start<IRouteOrder>(
            new Order("o-deep", 20m),
            replayDecisionsOf: previous
        );

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain($"more than {DecisionJournal.MaxReplayChain} runs");
        decider.Requests.Should().BeEmpty();
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task A_run_on_a_host_that_records_decisions_is_marked_as_recorded()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (train, _) = await Run<IRouteOrder>(new Order("o-marked", 20m));

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == train.Metadata!.Id))
            .DecisionsRecorded.Should()
            .BeTrue();
    }

    [Test]
    public async Task A_replay_of_a_run_that_did_not_record_its_decisions_fails_the_run_as_permanent()
    {
        // As a run on a host without AddDecisionRecording leaves it: it may have decided, but
        // nothing says what.
        var unrecorded = await Seed<IRouteOrder>(replayDecisionsOf: null);

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (train, run) = Start<IRouteOrder>(
            new Order("o-unrecorded", 20m),
            replayDecisionsOf: unrecorded
        );

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain($"run {unrecorded} ran without recording its decisions");
        decider.Requests.Should().BeEmpty("the run fails before it asks anything");
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task A_replay_of_a_recorded_run_that_reached_no_questions_asks_afresh()
    {
        var reachedNothing = await Seed<IRouteOrder>(
            replayDecisionsOf: null,
            decisionsRecorded: true
        );

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (_, output) = await Run<IRouteOrder>(
            new Order("o-nothing", 20m),
            replayDecisionsOf: reachedNothing
        );

        output.Should().Be("shipped");
        decider.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task A_decision_reported_after_the_train_changed_its_external_id_fails_as_permanent()
    {
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        Ship.Ran = 0;

        var (train, run) = Start<IRenameThenRoute>(new Order("o-renamed", 20m));

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("external id was changed");
        decider.Requests.Should().BeEmpty("the replay lookup that comes first already fails");
        Ship.Ran.Should().Be(0);
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
        (await Recorded(train.Metadata.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_replay_of_a_run_that_reached_fewer_questions_asks_the_rest_afresh()
    {
        Decider.Use(new SequenceDecider(Fulfilment.ManualCheck, Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteTwice>(new Order("o-fewer", 20m));

        // As if it had died before its second asking.
        await Forget(original.Metadata!.Id, occurrence: 1);

        var decider = Decider.Use(new SequenceDecider(Fulfilment.ManualCheck, Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteTwice>(
            new Order("o-fewer", 20m),
            replayDecisionsOf: original.Metadata!.Id
        );

        output.Should().Be("held for review");
        decider.Asked.Should().Be(1, "only the second asking was never answered");
        (await Recorded(requeued.Metadata!.Id))
            .OrderBy(d => d.Occurrence)
            .Select(d => d.Replayed)
            .Should()
            .Equal(true, false);
    }

    [Test]
    public async Task A_run_started_from_a_row_it_was_given_takes_the_rows_external_id()
    {
        // The scheduler creates the row before dispatch. Without adopting its id, the run's
        // failure data and decisions named an id that matched no row.
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (train, run) = Start<IRouteOrder>(new Order("o6", 20m), externalId: Id());
        await run();

        ((ServiceTrain<Order, string>)(object)train)
            .ExternalId.Should()
            .Be(train.Metadata!.ExternalId);
        (await Recorded(train.Metadata.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task A_shadow_that_answers_with_numbers_JSON_cannot_hold_does_not_cost_the_live_record()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (train, output) = await Run<IRouteWithShadow>(new Order("o-nan", 20m));

        output.Should().Be("shipped");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.Tracks().Should().Equal("Standard");
        decision.Shadows.Should().Contain(typeof(NonFiniteShadow).FullName);
        decision.Shadows.Should().Contain("\"confidence\":\"NaN\"");
        decision.Shadows.Should().Contain("\"Standard\":\"Infinity\"");
    }

    [Test]
    public async Task A_shadow_answer_that_cannot_be_read_back_is_recorded_as_an_error()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (train, output) = await Run<IRouteWithOddShadow>(new Order("o-odd", 20m));

        output.Should().Be("shipped");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.Shadows.Should().Contain(typeof(OddShadow).FullName);
        decision.Shadows.Should().Contain("its answer could not be recorded");
        decision.Shadows.Should().NotContain("\"type\":\"OddAnswer\"");
    }

    [Test]
    public async Task A_recorded_answer_with_a_damaged_number_fails_the_replay_as_permanent()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (original, _) = await Run<IRouteOrder>(new Order("o-damaged", 20m));

        await Rewrite(
            original.Metadata!.Id,
            d => d.Answer = """{"type":"choice","choice":"ManualCheck","confidence":"lots"}"""
        );

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (train, run) = Start<IRouteOrder>(
            new Order("o-damaged", 20m),
            replayDecisionsOf: original.Metadata.Id
        );

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("cannot be read")
            .And.Contain("'lots'");
        decider.Requests.Should().BeEmpty();
        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task Runs_sharing_an_external_id_each_record_and_replay_their_own()
    {
        // A retried dispatch can leave two rows under one external id, both running at once.
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        MeetBeforeDeciding.Expect(1);
        var (source, _) = await Run<IMeetThenRoute>(new Order("o-shared", 20m));

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var shared = Id();
        MeetBeforeDeciding.Expect(2);

        var (replaying, replayRun) = Start<IMeetThenRoute>(
            new Order("o-shared", 20m),
            externalId: shared,
            replayDecisionsOf: source.Metadata!.Id
        );
        var (asking, askRun) = Start<IMeetThenRoute>(
            new Order("o-shared", 20m),
            externalId: shared
        );

        var outputs = await Task.WhenAll(replayRun(), askRun()).WaitAsync(Wait);

        outputs.Should().Equal("held for review", "shipped");
        decider.Requests.Should().ContainSingle("only the run that replays nothing asks");
        replaying.Metadata!.Id.Should().NotBe(asking.Metadata!.Id);

        var replayed = (await Recorded(replaying.Metadata.Id)).Should().ContainSingle().Subject;
        replayed.Replayed.Should().BeTrue();
        replayed.Tracks().Should().Equal("ManualCheck");

        var asked = (await Recorded(asking.Metadata.Id)).Should().ContainSingle().Subject;
        asked.Replayed.Should().BeFalse();
        asked.Tracks().Should().Equal("Standard");
    }

    [Test]
    public async Task A_replay_ends_with_its_run_and_never_reaches_a_later_run_under_the_same_id()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (source, first) = Start<IRouteThenFail>(new Order("o-later", 20m));
        await first.Should().ThrowAsync<InvalidOperationException>();
        var shared = Id();

        var (_, failing) = Start<IRouteThenFail>(
            new Order("o-later", 20m),
            externalId: shared,
            replayDecisionsOf: source.Metadata!.Id
        );
        await failing.Should().ThrowAsync<InvalidOperationException>();

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (later, run) = Start<IRouteThenFail>(new Order("o-later", 20m), externalId: shared);

        await run.Should().ThrowAsync<InvalidOperationException>();
        decider.Requests.Should().ContainSingle();
        (await Recorded(later.Metadata!.Id))
            .Should()
            .ContainSingle()
            .Which.Replayed.Should()
            .BeFalse();
    }

    [Test]
    public async Task A_run_has_decisions_to_replay_when_it_recorded_one_or_replays_another()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (decided, _) = await Run<IRouteOrder>(new Order("o-has", 20m));
        var failedRequeue = await Seed<IRouteOrder>(replayDecisionsOf: decided.Metadata!.Id);
        var undecided = await Seed<IRouteOrder>(replayDecisionsOf: null, decisionsRecorded: true);

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        (await context.HasDecisionsToReplay(decided.Metadata.Id, CancellationToken.None))
            .Should()
            .BeTrue();
        (await context.HasDecisionsToReplay(failedRequeue, CancellationToken.None))
            .Should()
            .BeTrue("its requeue replays the answers of the run it replayed");
        (await context.HasDecisionsToReplay(undecided, CancellationToken.None)).Should().BeFalse();
        (await context.HasDecisionsToReplay(987_654_322, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task An_answer_the_run_refuses_is_recorded_with_why_and_takes_no_track()
    {
        var decider = Decider.Use(
            new ScriptedDecider().Answer(
                QuestionKey.For<Fulfilment>(),
                _ => new ChoiceAnswer("Banana", 0.9) { Model = "jev-1.13.0" }
            )
        );
        Ship.Ran = 0;

        var (train, run) = Start<IRouteOrder>(new Order("o-refused", 20m));

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("'Banana', which is not one of its options");
        train.Metadata!.FailureClass.Should().Be(FailureClass.Transient);
        Ship.Ran.Should().Be(0);
        decider.Requests.Should().ContainSingle();

        var row = (await Recorded(train.Metadata.Id)).Should().ContainSingle().Subject;
        row.QuestionKey.Should().Be(QuestionKey.For<Fulfilment>());
        row.Occurrence.Should().Be(0);
        row.Fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        row.Answer.Should().Contain("\"choice\":\"Banana\"");
        row.Model.Should().Be("jev-1.13.0");
        row.Decider.Should().Be(typeof(DeciderSlot).FullName);
        row.Refused.Should()
            .Be(
                $"the decider answered '{QuestionKey.For<Fulfilment>()}' with 'Banana', which is "
                    + "not one of its options"
            );
        row.Replayed.Should().BeFalse();
        row.Routes.Should().BeNull("no track was taken on it");
    }

    [Test]
    public async Task A_missing_answer_is_recorded_as_refused_with_no_answer()
    {
        Decider.Use(new ScriptedDecider());

        var (train, run) = Start<IRouteOrder>(new Order("o-unanswered", 20m));

        await run.Should().ThrowAsync<TrainException>();
        var row = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        row.Answer.Should().BeNull();
        row.Refused.Should().Be($"the decider gave no answer to '{QuestionKey.For<Fulfilment>()}'");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_requeue_of_a_run_that_failed_on_a_refused_answer_asks_afresh(bool answered)
    {
        Decider.Use(
            answered
                ? new ScriptedDecider().Answer(
                    QuestionKey.For<Fulfilment>(),
                    _ => new ChoiceAnswer("Banana")
                )
                : new ScriptedDecider()
        );
        var (original, failing) = Start<IRouteOrder>(new Order("o-refused-requeue", 20m));
        await failing.Should().ThrowAsync<TrainException>();

        using (var scope = _provider.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IDataContext>()
                    .HasDecisionsToReplay(original.Metadata!.Id, CancellationToken.None)
            )
                .Should()
                .BeFalse("a refused answer is nothing to replay");

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var (requeued, output) = await Run<IRouteOrder>(
            new Order("o-refused-requeue", 20m),
            replayDecisionsOf: original.Metadata!.Id
        );

        output.Should().Be("shipped");
        decider.Requests.Should().ContainSingle("the refused answer is not replayed");
        var row = (await Recorded(requeued.Metadata!.Id)).Should().ContainSingle().Subject;
        row.Replayed.Should().BeFalse();
        row.Refused.Should().BeNull();
        row.Answer.Should()
            .NotContain("replay_refused", "the refused answer was never offered for replay");
        (await Recorded(original.Metadata.Id))
            .Should()
            .ContainSingle()
            .Which.Refused.Should()
            .NotBeNull("the failed run keeps its own record");
    }

    [Test]
    public async Task A_decision_made_after_code_in_the_run_lost_its_flow_is_recorded_against_the_run()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));

        var (train, output) = await Run<ILoseFlowThenRoute>(new Order("o-lost", 20m));

        output.Should().Be("held for review");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.Replayed.Should().BeFalse();
        decision.Tracks().Should().Equal(["ManualCheck"], "the routing is added to the same row");
    }

    [Test]
    public async Task A_branch_routing_on_a_decision_made_before_the_fork_after_the_flow_was_lost_records_its_track()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));

        var (train, output) = await Run<ILoseFlowThenForkAndRoute>(new Order("o-lost-fork", 20m));

        output.Should().Be("held for review|packed");
        var decision = (await Recorded(train.Metadata!.Id)).Should().ContainSingle().Subject;
        decision.BranchPath.Should().BeEmpty("the question was asked before the fork");
        decision
            .Tracks()
            .Should()
            .Equal(
                ["ManualCheck"],
                "the branch's routing is added to the row of the decision it routes on, which "
                    + "the branch inherited from the chain it was forked from"
            );
    }

    [Test]
    public async Task A_requeue_that_loses_its_flow_still_replays_the_runs_decisions()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.ManualCheck));
        var (source, _) = await Run<ILoseFlowThenRoute>(new Order("o-lost-replay", 20m));

        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var (requeue, output) = await Run<ILoseFlowThenRoute>(
            new Order("o-lost-replay", 20m),
            replayDecisionsOf: source.Metadata!.Id
        );

        output.Should().Be("held for review");
        decider.Requests.Should().BeEmpty("the answer is replayed, not asked again");
        var replayed = (await Recorded(requeue.Metadata!.Id)).Should().ContainSingle().Subject;
        replayed.Replayed.Should().BeTrue();
        replayed.Tracks().Should().Equal("ManualCheck");
    }

    [Test]
    public async Task Runs_sharing_an_external_id_that_both_lose_their_flow_are_not_guessed_between()
    {
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        var shared = Id();
        MeetBeforeDeciding.Expect(2);
        MeetAfterDeciding.Expect(2);

        var (first, firstRun) = Start<IMeetThenLoseFlow>(
            new Order("o-lost-shared", 20m),
            externalId: shared
        );
        var (second, secondRun) = Start<IMeetThenLoseFlow>(
            new Order("o-lost-shared", 20m),
            externalId: shared
        );

        var outputs = await Task.WhenAll(firstRun(), secondRun()).WaitAsync(Wait);

        outputs.Should().Equal("shipped", "shipped");
        decider.Requests.Should().HaveCount(2);
        (await Recorded(first.Metadata!.Id)).Should().BeEmpty("either row could be the one");
        (await Recorded(second.Metadata!.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_plain_train_deciding_off_the_runs_flow_under_its_external_id_is_not_recorded_against_it()
    {
        Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard).YesNo<Rush>(0.9));
        var shared = Id();
        DecideInAPlainTrainOffFlow.ExternalId = shared;

        var (train, run) = Start<IRunAPlainTrainOffFlow>(new Order("o-plain", 20m), shared);

        (await run().WaitAsync(Wait)).Should().Be("shipped");
        DecideInAPlainTrainOffFlow.Decided.Should().BeTrue("the plain train did decide");
        (await Recorded(train.Metadata!.Id))
            .Select(d => d.QuestionKey)
            .Should()
            .Equal(["Fulfilment"], "only the run's own decision is its");
    }

    [Test]
    public void Adding_decision_recording_twice_registers_it_once()
    {
        _provider.GetServices<IDecisionObserver>().Should().ContainSingle();
        _provider.GetServices<IDecisionReplay>().Should().ContainSingle();
        _provider.GetServices<DecisionJournal>().Should().ContainSingle();
    }

    private async Task<(TTrain Train, string Output)> Run<TTrain>(
        Order order,
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<Order, string>
    {
        if (replayDecisionsOf is null)
        {
            using var scope = _provider.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService<TTrain>();
            return (train, await train.Run(order));
        }

        var (started, run) = Start<TTrain>(order, replayDecisionsOf: replayDecisionsOf);
        return (started, await run());
    }

    private (TTrain Train, Func<Task<string>> Run) Start<TTrain>(
        Order order,
        string? externalId = null,
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<Order, string>
    {
        var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<Order, string>)(object)scope.ServiceProvider.GetRequiredService<TTrain>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = externalId ?? Id(),
                Input = order,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        return (
            (TTrain)(object)train,
            async () =>
            {
                using (scope)
                    return await train.Run(order, metadata);
            }
        );
    }

    private static string Id() => Guid.NewGuid().ToString("N");

    /// <summary>A run of <typeparamref name="TTrain"/> that never reached a question.</summary>
    private async Task<long> Seed<TTrain>(
        long? replayDecisionsOf,
        bool decisionsRecorded = false,
        bool replayAbandoned = false,
        long? manifestId = null
    )
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Id(),
                Input = new Order("seeded", 1m),
                ReplayDecisionsOf = replayDecisionsOf,
                ManifestId = manifestId,
            }
        );

        metadata.DecisionsRecorded = decisionsRecorded;
        metadata.ReplayAbandoned = replayAbandoned;
        context.Metadatas.Add(metadata);
        await context.SaveChanges(CancellationToken.None);
        return metadata.Id;
    }

    private async Task Relink(long metadataId, long replayDecisionsOf)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var metadata = await context.Metadatas.SingleAsync(m => m.Id == metadataId);
        metadata.ReplayDecisionsOf = replayDecisionsOf;
        await context.SaveChanges(CancellationToken.None);
    }

    private async Task Forget(long metadataId, int occurrence)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        context.RecordedDecisions.RemoveRange(
            context.RecordedDecisions.Where(d =>
                d.MetadataId == metadataId && d.Occurrence == occurrence
            )
        );

        await context.SaveChanges(CancellationToken.None);
    }

    private async Task Rewrite(
        long metadataId,
        Action<Models.RecordedDecision.RecordedDecision> change
    )
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        foreach (var decision in context.RecordedDecisions.Where(d => d.MetadataId == metadataId))
            change(decision);

        await context.SaveChanges(CancellationToken.None);
    }

    private async Task<List<Models.RecordedDecision.RecordedDecision>> Recorded(long metadataId)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .ToListAsync();
    }
}

/// <summary>
/// A decision that cannot be written fails its step, and a replay a host cannot honour fails its
/// run, rather than either going ahead without the record.
/// </summary>
public class DecisionRecordingFailureTests
{
    private static readonly DeciderSlot Decider = new();

    [Test]
    public async Task A_decision_that_cannot_be_written_fails_its_step_as_transient()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton(_ => new DecisionJournal(new DownFactory()))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        Ship.Ran = 0;

        using var scope = provider.CreateScope();
        var train = (RouteOrder)scope.ServiceProvider.GetRequiredService<IRouteOrder>();

        var run = () => train.Run(new Order("down", 20m));

        (await run.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be("the database is down");
        decider.Requests.Should().ContainSingle();
        Ship.Ran.Should().Be(0, "a decision that was not recorded is not acted on");
        train.Metadata!.FailureClass.Should().Be(FailureClass.Transient);
    }

    [Test]
    public async Task A_replay_on_a_host_that_records_no_decisions_fails_the_run_as_permanent()
    {
        await using var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider();
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        using var scope = provider.CreateScope();
        var train = (RouteOrder)scope.ServiceProvider.GetRequiredService<IRouteOrder>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRouteOrder).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new Order("unconfigured", 20m),
                ReplayDecisionsOf = 42,
            }
        );

        var run = () => train.Run(new Order("unconfigured", 20m), metadata);

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("does not record decisions (AddDecisionRecording)");
        train.Metadata!.DecisionsRecorded.Should().BeFalse();
        decider.Requests.Should().BeEmpty();
        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureClass.Should().Be(FailureClass.Permanent);
    }

    /// <summary>A store that cannot be reached.</summary>
    private sealed class DownFactory : IDataContextProviderFactory
    {
        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the database is down");

        public IEffectProvider Create() =>
            throw new InvalidOperationException("the database is down");
    }
}

/// <summary>The trains the decision recording tests run, and their registrations.</summary>
internal static class DecisionTrains
{
    public static IServiceCollection Register(IServiceCollection services, IDecider decider) =>
        services
            .AddSingleton(decider)
            .AddSingleton<NonFiniteShadow>()
            .AddScopedTraxRoute<IRouteOrder, RouteOrder>()
            .AddScopedTraxRoute<IRouteThenFail, RouteThenFail>()
            .AddScopedTraxRoute<IRouteThenPeek, RouteThenPeek>()
            .AddScopedTraxRoute<IRouteTwice, RouteTwice>()
            .AddScopedTraxRoute<IRouteWithShadow, RouteWithShadow>()
            .AddScopedTraxRoute<IRenameThenRoute, RenameThenRoute>()
            .AddScopedTraxRoute<IDecideThenSwitchTwice, DecideThenSwitchTwice>()
            .AddSingleton<OddShadow>()
            .AddScopedTraxRoute<IRouteWithOddShadow, RouteWithOddShadow>()
            .AddScopedTraxRoute<IMeetThenRoute, MeetThenRoute>()
            .AddScopedTraxRoute<ILoseFlowThenRoute, LoseFlowThenRoute>()
            .AddScopedTraxRoute<IMeetThenLoseFlow, MeetThenLoseFlow>()
            .AddScopedTraxRoute<ILoseFlowThenForkAndRoute, LoseFlowThenForkAndRoute>()
            .AddScopedTraxRoute<IRunAPlainTrainOffFlow, RunAPlainTrainOffFlow>()
            .AddScopedTraxRoute<IReadThenRoute, ReadThenRoute>();
}

/// <summary>The registered decider, swapped per test.</summary>
internal sealed class DeciderSlot : IDecider
{
    private IDecider _inner = new ScriptedDecider();

    public TDecider Use<TDecider>(TDecider inner)
        where TDecider : IDecider
    {
        _inner = inner;
        return inner;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
        _inner.Decide(request, ct);
}

/// <summary>Answers each asking of the fulfilment question with the next choice in turn.</summary>
internal sealed class SequenceDecider(params Fulfilment[] choices) : IDecider
{
    private int _asked;

    public int Asked => _asked;

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        var choice = choices[Interlocked.Increment(ref _asked) - 1];

        return Task.FromResult(
            new DecisionResult(
                new Dictionary<string, Answer>
                {
                    [QuestionKey.For<Fulfilment>()] = new ChoiceAnswer(choice.ToString()),
                }
            )
        );
    }
}

/// <summary>A shadow whose answer carries numbers JSON has no form for.</summary>
public sealed class NonFiniteShadow : IDecider
{
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
        Task.FromResult(
            new DecisionResult(
                new Dictionary<string, Answer>
                {
                    [QuestionKey.For<Fulfilment>()] = new ChoiceAnswer(
                        "Standard",
                        double.NaN,
                        new Dictionary<string, double> { ["Standard"] = double.PositiveInfinity }
                    ),
                }
            )
        );
}

/// <summary>An answer of a kind Trax.Core does not define.</summary>
public sealed record OddAnswer : Answer;

/// <summary>A shadow that answers with a kind of answer the journal has no stored form for.</summary>
public sealed class OddShadow : IDecider
{
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
        Task.FromResult(
            new DecisionResult(
                new Dictionary<string, Answer> { [QuestionKey.For<Fulfilment>()] = new OddAnswer() }
            )
        );
}

public interface IRouteWithOddShadow : IServiceTrain<Order, string>;

public class RouteWithOddShadow : ServiceTrain<Order, string>, IRouteWithOddShadow
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                    .Shadow<OddShadow>()
            )
            .Resolve();
}

public sealed record Order(string Id, decimal Total);

[Asks("How should this order be fulfilled?")]
public enum Fulfilment
{
    Standard,
    ManualCheck,
}

public interface IRouteOrder : IServiceTrain<Order, string>;

public class RouteOrder : ServiceTrain<Order, string>, IRouteOrder
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                    .RequireConfidence(0.8)
                    .Otherwise(t => t.Chain<HoldForReview>())
            )
            .Resolve();
}

public interface IDecideThenSwitchTwice : IServiceTrain<Order, string>;

/// <summary>Decides once and routes on the decision twice, with different bars.</summary>
public class DecideThenSwitchTwice : ServiceTrain<Order, string>, IDecideThenSwitchTwice
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Decide<Order>(q => q.Choice<Fulfilment>())
            .Switch<Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Switch<Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                    .RequireConfidence(0.95)
                    .Otherwise(t => t.Chain<HoldForReview>())
            )
            .Resolve();
}

public interface IRenameThenRoute : IServiceTrain<Order, string>;

/// <summary>Changes its own external id once it is running, then decides.</summary>
public class RenameThenRoute : ServiceTrain<Order, string>, IRenameThenRoute
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        ExternalId = Guid.NewGuid().ToString("N");

        return Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
    }
}

public interface IRouteWithShadow : IServiceTrain<Order, string>;

public class RouteWithShadow : ServiceTrain<Order, string>, IRouteWithShadow
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                    .Shadow<NonFiniteShadow>()
            )
            .Resolve();
}

public interface IRouteTwice : IServiceTrain<Order, string>;

/// <summary>Asks the same question twice, and the second answer decides the output.</summary>
public class RouteTwice : ServiceTrain<Order, string>, IRouteTwice
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
}

public interface IRouteThenFail : IServiceTrain<Order, string>;

public class RouteThenFail : ServiceTrain<Order, string>, IRouteThenFail
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Chain<FailAfterRouting>()
            .Resolve();
}

public interface IRouteThenPeek : IServiceTrain<Order, string>;

public class RouteThenPeek : ServiceTrain<Order, string>, IRouteThenPeek
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Chain<PeekDecisions>()
            .Resolve();
}

public interface IMeetThenRoute : IServiceTrain<Order, string>;

/// <summary>Waits for the other runs it is expected with, so they decide while all are running.</summary>
public class MeetThenRoute : ServiceTrain<Order, string>, IMeetThenRoute
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<MeetBeforeDeciding>()
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
}

public interface ILoseFlowThenRoute : IServiceTrain<Order, string>;

/// <summary>Builds its chain with its run's async flow suppressed, then decides.</summary>
public class LoseFlowThenRoute : ServiceTrain<Order, string>, ILoseFlowThenRoute
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        var built = LoseTheFlow.Now();

        var chain = Chain(new AfterTheChainIsBuilt(built.Task))
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();

        built.SetResult();
        return chain;
    }
}

public interface ILoseFlowThenForkAndRoute : IServiceTrain<Order, string>;

/// <summary>
/// Builds its chain with its run's async flow suppressed, decides, then forks, and one branch
/// routes on the decision made before the fork.
/// </summary>
public class LoseFlowThenForkAndRoute : ServiceTrain<Order, string>, ILoseFlowThenForkAndRoute
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        var built = LoseTheFlow.Now();

        var chain = Chain(new AfterTheChainIsBuilt(built.Task))
            .Decide<Order>(q => q.Choice<Fulfilment>())
            .Parallel(p =>
                p.Branch(
                        "routes",
                        b =>
                            b.Switch<Fulfilment>(tracks =>
                                tracks
                                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                            )
                    )
                    .Branch("packs", b => b.Chain<PackOrder>())
            )
            .Chain<JoinRoutedAndPacked>()
            .Resolve();

        built.SetResult();
        return chain;
    }
}

public sealed record Packed(string Note);

public class PackOrder : Junction<Order, Packed>
{
    public override Task<Packed> Run(Order input) => Task.FromResult(new Packed("packed"));
}

public class JoinRoutedAndPacked : Junction<(string, Packed), string>
{
    public override Task<string> Run((string, Packed) input) =>
        Task.FromResult($"{input.Item1}|{input.Item2.Note}");
}

public interface IMeetThenLoseFlow : IServiceTrain<Order, string>;

/// <summary>
/// Builds its chain with its run's async flow suppressed, waits for the other run sharing its
/// external id, then decides, and waits again before finishing so both runs are still in progress
/// while each decision is looked up.
/// </summary>
public class MeetThenLoseFlow : ServiceTrain<Order, string>, IMeetThenLoseFlow
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        var built = LoseTheFlow.Now();

        var chain = Chain(new AfterTheChainIsBuilt(built.Task))
            .Chain<MeetBeforeDeciding>()
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<MeetAfterDeciding>().Chain<Ship>())
                    .When(
                        Fulfilment.ManualCheck,
                        t => t.Chain<MeetAfterDeciding>().Chain<HoldForReview>()
                    )
            )
            .Resolve();

        built.SetResult();
        return chain;
    }
}

/// <summary>
/// Suppresses ExecutionContext flow and leaves it suppressed, as code that forgets to restore it
/// does. Every step of a chain built after it captures no flow, so each resumes without the run's.
/// </summary>
public static class LoseTheFlow
{
    /// <returns>A signal to set once the chain is built, which nothing runs on inline.</returns>
    public static TaskCompletionSource Now()
    {
        ExecutionContext.SuppressFlow();
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// Holds the chain until it is fully built, so no step after it can run on the thread that built
/// it, which still carries the run's flow.
/// </summary>
public class AfterTheChainIsBuilt(Task built) : Junction<Order, Order>
{
    public override async Task<Order> Run(Order input)
    {
        await built.WaitAsync(TimeSpan.FromSeconds(30));
        return input;
    }
}

[Asks("Is it a rush order?")]
public sealed class Rush;

public interface IRunAPlainTrainOffFlow : IServiceTrain<Order, string>;

/// <summary>Runs a plain train that decides, off its own flow, then decides itself.</summary>
public class RunAPlainTrainOffFlow : ServiceTrain<Order, string>, IRunAPlainTrainOffFlow
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<DecideInAPlainTrainOffFlow>()
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
}

/// <summary>
/// Runs a plain train on a thread that does not carry the run's flow, under the run's external id,
/// reporting to the host's journal.
/// </summary>
public class DecideInAPlainTrainOffFlow(
    IDecider decider,
    IDecisionObserver observer,
    IDecisionReplay replay
) : Junction<Order, Order>
{
    public static string ExternalId { get; set; } = "";

    public static bool Decided { get; private set; }

    public override async Task<Order> Run(Order input)
    {
        Decided = false;
        Task<Order> plain;

        using (ExecutionContext.SuppressFlow())
            plain = Task.Run(() =>
                new PlainRush(decider, observer, replay) { ExternalId = ExternalId }.Run(input)
            );

        var output = await plain;
        Decided = true;
        return output;
    }
}

/// <summary>A plain train that decides whether an order is a rush.</summary>
public class PlainRush(IDecider decider, IDecisionObserver observer, IDecisionReplay replay)
    : Trax.Core.Train.Train<Order, Order>
{
    protected override Task<Either<Exception, Order>> Junctions() =>
        AddServices(decider, observer, replay).Decide<Order>(q => q.YesNo<Rush>()).Resolve();
}

public class Ship : Junction<Order, string>
{
    public static int Ran;

    public override Task<string> Run(Order input)
    {
        Interlocked.Increment(ref Ran);
        return Task.FromResult("shipped");
    }
}

public class HoldForReview : Junction<Order, string>
{
    public override Task<string> Run(Order input) => Task.FromResult("held for review");
}

public class FailAfterRouting : Junction<string, string>
{
    public override Task<string> Run(string input) =>
        throw new InvalidOperationException("the warehouse is offline");
}

/// <summary>
/// Holds each run until the expected number have arrived. Each subclass is a barrier of its own,
/// since a generic type's statics are per closed type.
/// </summary>
public abstract class Meeting<TSelf> : Junction<Order, Order>
    where TSelf : Meeting<TSelf>
{
    private static int _expected;
    private static int _arrived;
    private static TaskCompletionSource _all = new();

    public static void Expect(int runs)
    {
        _expected = runs;
        _arrived = 0;
        _all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async Task<Order> Run(Order input)
    {
        var all = _all;

        if (Interlocked.Increment(ref _arrived) == _expected)
            all.TrySetResult();

        await all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return input;
    }
}

public class MeetBeforeDeciding : Meeting<MeetBeforeDeciding>;

/// <summary>
/// Holds each run on its track until every run has decided, so none completes while another's
/// decision is still being looked up by external id.
/// </summary>
public class MeetAfterDeciding : Meeting<MeetAfterDeciding>;

/// <summary>Reads the run's decisions through a context of its own, as another process would.</summary>
public class PeekDecisions(IDataContextProviderFactory factory) : EffectJunction<string, string>
{
    public static List<(long MetadataId, TrainState State, string? Track)> Seen { get; } = [];

    public override async Task<string> Run(string input)
    {
        Seen.Clear();
        var id = Metadata!.TrainMetadataId;
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        var state = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => m.TrainState)
            .SingleAsync();

        foreach (
            var decision in await context
                .RecordedDecisions.AsNoTracking()
                .Where(d => d.MetadataId == id)
                .ToListAsync()
        )
            Seen.Add((decision.MetadataId, state, decision.Tracks().SingleOrDefault()));

        return input;
    }
}

/// <summary>Reads the tracks out of a recorded decision's routes.</summary>
internal static class RecordedRoutes
{
    public static IReadOnlyList<string> Tracks(
        this Models.RecordedDecision.RecordedDecision decision
    ) =>
        decision.Routes is null
            ? []
            : System
                .Text.Json.Nodes.JsonNode.Parse(decision.Routes)!
                .AsArray()
                .Select(route => (string)route!["track"]!)
                .ToList();
}

/// <summary>What the stock system says the order's total is, read by a junction before deciding.</summary>
public static class StockLevel
{
    public static decimal Total { get; set; }
}

public class ReadStock : Junction<Order, Order>
{
    public override Task<Order> Run(Order input) =>
        Task.FromResult(input with { Total = StockLevel.Total });
}

public interface IReadThenRoute : IServiceTrain<Order, string>;

/// <summary>Reads a value that can change between a run and its requeue, then decides on it.</summary>
public class ReadThenRoute : ServiceTrain<Order, string>, IReadThenRoute
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ReadStock>()
            .Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
}
