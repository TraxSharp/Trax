using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// With <c>AddJunctionEvents</c>, each step of a run (a junction starting and ending, a question
/// answered, a track taken) is published live and stored in <c>trax.junction_run</c>, carrying
/// names, times, states and failure classes, and never what the run was given or produced.
///
/// <para>Enforces docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md.</para>
/// </summary>
[Property("adr", "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md")]
public class JunctionEventsTests
{
    private const string Adr = "docs/adr/0019-junction-events-are-opt-in-and-carry-no-run-data.md";

    private ServiceProvider _provider = null!;

    private static readonly CapturingHandler Handler = new();
    private static readonly CapturingBroadcaster Broadcaster = new();
    private static readonly SwitchableDecider Decider = new();

    [OneTimeSetUp]
    public void Build() =>
        _provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler>(Handler)
            .AddSingleton<ITrainEventBroadcaster>(Broadcaster)
            .AddSingleton<IFailureClassifier, WarehouseClassifier>()
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddJunctionEvents().AddJunctionEvents()
                )
            )
            .BuildServiceProvider();

    [OneTimeTearDown]
    public async Task Dispose() => await _provider.DisposeAsync();

    [Test]
    public async Task A_run_publishes_each_junction_and_decision_in_order()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var (metadataId, output) = await Run<ILaneTrain>();

        output.Should().Be("loaded|stamped");
        var events = Handler.For(metadataId);
        events
            .Should()
            .HaveCount(8)
            .And.OnlyContain(
                e => e.Junction!.Attempt == null,
                $"a run with no manifest is no attempt of anything. See {Adr}."
            );
        events
            .Select(e => (e.EventType, e.Junction!.Position, e.Junction.Name))
            .Should()
            .Equal(
                ("JunctionStarted", 0, nameof(Weigh)),
                ("JunctionCompleted", 0, nameof(Weigh)),
                ("Decided", 1, "Lane"),
                ("Routed", 2, "Lane"),
                ("JunctionStarted", 3, nameof(Load)),
                ("JunctionCompleted", 3, nameof(Load)),
                ("JunctionStarted", 4, nameof(Stamp)),
                ("JunctionCompleted", 4, nameof(Stamp))
            );

        events
            .Should()
            .OnlyContain(e =>
                e.TrainName == typeof(ILaneTrain).FullName
                && e.TrainState == "InProgress"
                && e.Output == null
                && e.FailureReason == null
            );

        var decided = events.Single(e => e.EventType == "Decided").Junction!;
        decided.Kind.Should().Be(JunctionRunKind.Choice);
        decided.State.Should().Be(JunctionRunState.Completed);
        decided.QuestionKey.Should().Be("Lane");
        decided.Answer.Should().Be("Express");
        decided.Confidence.Should().Be(0.9);
        decided.Replayed.Should().BeFalse();
        decided.Decider.Should().Be(typeof(SwitchableDecider).FullName);

        var routed = events.Single(e => e.EventType == "Routed").Junction!;
        routed.Kind.Should().Be(JunctionRunKind.Route);
        routed.Answer.Should().Be("Express");

        var load = events.Where(e => e.Junction!.Name == nameof(Load)).ToList();
        load[0].Junction!.State.Should().Be(JunctionRunState.InProgress);
        load[0].Junction!.EndedAt.Should().BeNull();
        load[1].Junction!.State.Should().Be(JunctionRunState.Completed);
        load[1].Junction!.EndedAt.Should().NotBeNull();
        load[1].Junction!.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        load[1].Junction!.StartedAt.Should().Be(load[0].Junction!.StartedAt);

        Broadcaster
            .For(metadataId)
            .Select(e => e.EventType)
            .Should()
            .Equal(events.Select(e => e.EventType), "the same steps go over the transport");
    }

    [Test]
    public async Task A_failing_junction_publishes_its_class_and_exception_type_and_the_skipped_one_nothing()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Ground, 0.8));

        var (metadataId, _) = await Run<ILaneThenFailTrain>(expectFailure: true);

        var events = Handler.For(metadataId);
        var failed = events.Should().ContainSingle(e => e.EventType == "JunctionFailed").Subject;
        failed.Junction!.Name.Should().Be(nameof(Explode));
        failed.Junction.State.Should().Be(JunctionRunState.Failed);
        failed.Junction.FailureClass.Should().Be(FailureClass.Permanent);
        failed.Junction.FailureException.Should().Be(nameof(InvalidOperationException));
        failed.Junction.EndedAt.Should().NotBeNull();
        events.Should().NotContain(e => e.Junction!.Name == nameof(NeverReached));
        events.Last().Should().BeSameAs(failed);
    }

    [Test]
    public async Task The_timeline_is_stored_and_read_back_in_order()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Ground, 0.8));

        var (metadataId, _) = await Run<ILaneThenFailTrain>(expectFailure: true);

        var rows = await Rows(metadataId);
        rows.Select(r => (r.Position, r.Kind, r.Name, r.State))
            .Should()
            .Equal(
                (0, JunctionRunKind.Junction, nameof(Weigh), JunctionRunState.Completed),
                (1, JunctionRunKind.Choice, "Lane", JunctionRunState.Completed),
                (2, JunctionRunKind.Route, "Lane", JunctionRunState.Completed),
                (3, JunctionRunKind.Junction, nameof(LoadSecret), JunctionRunState.Completed),
                (4, JunctionRunKind.Junction, nameof(Stamp), JunctionRunState.Completed),
                (5, JunctionRunKind.Junction, nameof(Explode), JunctionRunState.Failed)
            );

        rows[1].Answer.Should().Be("Ground");
        rows[1].Confidence.Should().Be(0.8);
        rows[2].Answer.Should().Be("Ground");
        rows[5].FailureClass.Should().Be(FailureClass.Permanent);
        rows[5].FailureException.Should().Be(nameof(InvalidOperationException));
        rows.Where(r => r.Kind == JunctionRunKind.Junction)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(r => r.EndedAt != null && r.EndedAt >= r.StartedAt);
    }

    [Test]
    public async Task Nothing_the_run_was_given_or_produced_is_published_or_stored()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Ground, 0.8));

        var (metadataId, _) = await Run<ILaneThenFailTrain>(expectFailure: true);

        var wire = Broadcaster.For(metadataId).Select(m => JsonSerializer.Serialize(m)).ToList();
        var local = Handler.For(metadataId).Select(m => JsonSerializer.Serialize(m)).ToList();
        var stored = (await Rows(metadataId)).Select(r => JsonSerializer.Serialize(r)).ToList();

        wire.Should().NotBeEmpty();
        local.Should().NotBeEmpty();
        stored.Should().NotBeEmpty();
        foreach (var json in wire.Concat(local).Concat(stored))
        {
            json.Should()
                .NotContain(
                    JunctionEventTrains.InputSecret,
                    $"a train's input is never published. See {Adr}."
                );
            json.Should()
                .NotContain(
                    JunctionEventTrains.OutputSecret,
                    $"a junction's output is never published. See {Adr}."
                );
            json.Should()
                .NotContain(
                    JunctionEventTrains.FailureSecret,
                    $"a failure's message is never published. See {Adr}."
                );
            json.Should()
                .NotContain(
                    "Which lane",
                    $"a question's instructions are never published. See {Adr}."
                );
        }
    }

    [Test]
    public async Task The_answer_to_a_question_about_a_sensitive_type_is_withheld()
    {
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        var (metadataId, _) = await Run<ICustomsTrain>();

        var events = Handler.For(metadataId);
        var decided = events.Single(e => e.EventType == "Decided").Junction!;
        decided.QuestionKey.Should().Be("CustomsTier");
        decided.AnswerWithheld.Should().BeTrue();
        decided.Answer.Should().BeNull();
        decided.Confidence.Should().BeNull();

        var routed = events.Single(e => e.EventType == "Routed").Junction!;
        routed.AnswerWithheld.Should().BeTrue();
        routed.Answer.Should().BeNull($"the track taken would give the answer away. See {Adr}.");

        var wire = Broadcaster.For(metadataId).Select(m => JsonSerializer.Serialize(m)).ToList();
        wire.Should().NotBeEmpty();
        foreach (var json in wire)
            json.Should().NotContain("\"Red\"");

        (await Rows(metadataId))
            .Where(r => r.Kind != JunctionRunKind.Junction)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(r => r.AnswerWithheld && r.Answer == null && r.Confidence == null);
    }

    [Test]
    public async Task The_answer_to_a_closed_form_of_a_sensitive_generic_question_is_withheld()
    {
        Decider.Use(new ScriptedDecider().YesNo<Held<Refund>>(0.9));

        var (metadataId, _) = await Run<IHeldTrain>();

        AssertWithheld(metadataId, "Held<Refund>");
        (await Rows(metadataId))
            .Where(r => r.Kind != JunctionRunKind.Junction)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(r => r.AnswerWithheld && r.Answer == null && r.Confidence == null);
    }

    [Test]
    public async Task The_answer_to_a_question_about_a_type_that_inherits_the_mark_is_withheld()
    {
        Decider.Use(new ScriptedDecider().YesNo<Audit>(0.2));

        var (metadataId, _) = await Run<IAuditTrain>();

        AssertWithheld(metadataId, "Audit");
    }

    private void AssertWithheld(long metadataId, string key)
    {
        var steps = Handler
            .For(metadataId)
            .Where(e => e.EventType is "Decided" or "Routed")
            .Select(e => e.Junction!)
            .ToList();

        steps.Should().HaveCount(2);
        steps
            .Should()
            .OnlyContain(
                s =>
                    s.QuestionKey == key
                    && s.AnswerWithheld
                    && s.Answer == null
                    && s.Confidence == null,
                $"a question about a marked type has its answer and track withheld. See {Adr}."
            );
    }

    [Test]
    public async Task A_run_that_was_never_persisted_has_no_junction_events()
    {
        var publisher = _provider.GetRequiredService<JunctionEventPublisher>();
        var unsaved = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ILaneTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = JunctionEventTrains.Parcel(),
            }
        );

        var run = await publisher.BeginAsync(
            unsaved,
            typeof(LaneTrain),
            _provider,
            CancellationToken.None
        );

        run.Should().BeNull($"a run with no row publishes and records no steps. See {Adr}.");
    }

    [Test]
    public async Task A_question_about_a_type_the_scan_never_saw_is_withheld_by_its_type()
    {
        // A subclass of a marked type, made where no assembly scan reaches it, so only the type
        // Trax.Core reports can tell that it is sensitive.
        var hidden = AssemblyBuilder
            .DefineDynamicAssembly(
                new AssemblyName($"Hidden{Guid.NewGuid():N}"),
                AssemblyBuilderAccess.Run
            )
            .DefineDynamicModule("Hidden")
            .DefineType(
                "HiddenAudit",
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(SensitiveQuestion)
            )
            .CreateType();
        var run = await PersistedRun();
        // Set here, in the test's own frame, as ServiceTrain.Run sets it in the run's.
        JunctionEventRun.Current = run;
        var metadataId = run.Metadata.Id;
        var observer = new JunctionEventDecisionObserver();

        await observer.Decided(
            new DecisionMade(
                DecisionRun.NameOf(typeof(LaneTrain)),
                _externalId,
                new YesNoQuestion("HiddenAudit", "Audit it?", null, null),
                0,
                new string('0', 64),
                new YesNoAnswer(0.97),
                typeof(SwitchableDecider),
                false,
                []
            )
            {
                QuestionType = hidden,
            },
            CancellationToken.None
        );
        JunctionEventRun.Current = null;

        var decided = Handler.For(metadataId).Should().ContainSingle().Subject.Junction!;
        decided
            .AnswerWithheld.Should()
            .BeTrue($"the question's type inherits the mark. See {Adr}.");
        decided.Answer.Should().BeNull();
    }

    private string _externalId = "";

    /// <summary>The junction events of a persisted run of the lane train.</summary>
    private async Task<JunctionEventRun> PersistedRun()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ILaneTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = JunctionEventTrains.Parcel(),
            }
        );
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        _externalId = metadata.ExternalId;

        return (
            await _provider
                .GetRequiredService<JunctionEventPublisher>()
                .BeginAsync(metadata, typeof(LaneTrain), _provider, CancellationToken.None)
        )!;
    }

    [Test]
    public async Task The_decision_log_withholds_the_answer_to_a_sensitive_question()
    {
        var logger = new CapturingJournalLogger();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<ILogger<DecisionJournal>>(logger)
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        using var scope = provider.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<ICustomsTrain>()
            .Run(JunctionEventTrains.Parcel());

        logger.Messages.Should().Contain(m => m.Contains("withheld"));
        logger
            .Messages.Should()
            .NotContain(
                m => m.Contains("Red"),
                $"a sensitive question's answer and track stay out of the log. See {Adr}."
            );
    }

    [Test]
    public async Task The_decision_log_withholds_the_decisions_of_a_withheld_track()
    {
        var logger = new CapturingJournalLogger();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<ILogger<DecisionJournal>>(logger)
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99).Choose(Lane.Express, 0.9));

        using var scope = provider.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IInspectionTrain>()
            .Run(JunctionEventTrains.Parcel());

        logger
            .Messages.Should()
            .Contain(m => m.Contains(nameof(CustomsTier)), "the sensitive question's key is logged")
            .And.Contain(
                m => m.Contains("track whose answer is withheld"),
                "the Lane question was asked on the Red track"
            );
        logger
            .Messages.Should()
            .NotContain(
                m =>
                    m.Contains(nameof(Lane))
                    || m.Contains(nameof(Lane.Express))
                    || m.Contains(nameof(CustomsTier.Red)),
                $"what a withheld track asks and where it goes would give it away. See {Adr}."
            );
    }

    [Test]
    public async Task The_decision_log_withholds_why_a_sensitive_answer_was_not_replayed()
    {
        var logger = new CapturingJournalLogger();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<ILogger<DecisionJournal>>(logger)
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        var journal = provider.GetRequiredService<DecisionJournal>();

        // Outside a run, so it is logged only.
        await journal.Decided(
            new DecisionMade(
                "Some.Train",
                "run-1",
                new ChoiceQuestion(
                    "CustomsTier",
                    "Which tier?",
                    [new Criterion("Green", null), new Criterion("Red", null)]
                ),
                0,
                "fp",
                new ChoiceAnswer("Green", 0.9),
                typeof(SwitchableDecider),
                false,
                [],
                ReplayRefused: "the recorded answer 'Red' was given about a different state"
            )
            {
                QuestionType = typeof(CustomsTier),
            },
            CancellationToken.None
        );

        logger.Messages.Should().ContainSingle().Which.Should().Contain("not replayed: (withheld");
        logger
            .Messages.Should()
            .NotContain(
                m => m.Contains("Red"),
                $"why a sensitive answer was not replayed can quote it. See {Adr}."
            );
    }

    [Test]
    public async Task Junctions_on_a_track_whose_answer_is_withheld_have_their_names_withheld()
    {
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        var (metadataId, _) = await Run<ICustomsThenStampTrain>();

        var junctions = Handler
            .For(metadataId)
            .Select(e => e.Junction!)
            .Where(j => j.Kind == JunctionRunKind.Junction)
            .ToList();
        var route = Handler.For(metadataId).Single(e => e.EventType == "Routed").Junction!.Position;

        junctions
            .Where(j => j.Position < route)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(j => j.Name == nameof(Weigh) && j.TrackPosition == null);
        junctions
            .Where(j => j.Position > route)
            .Should()
            .HaveCount(4)
            .And.OnlyContain(
                j =>
                    j.NameWithheld
                    && j.Name == JunctionEventPayload.WithheldName
                    && j.TrackPosition == route,
                $"which junctions ran would give a withheld answer away. See {Adr}."
            );
        (await Rows(metadataId))
            .Where(r => r.Position > route)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(r =>
                r.NameWithheld
                && r.Name == JunctionEventPayload.WithheldName
                && r.TrackPosition == route
            );
    }

    [Test]
    public async Task Questions_and_routes_on_a_track_whose_answer_is_withheld_are_withheld_too()
    {
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99).Choose(Lane.Express, 0.9));

        var (metadataId, _) = await Run<IInspectionTrain>();

        var steps = Handler.For(metadataId).Select(e => e.Junction!).ToList();
        var customs = steps.Single(s => s.Kind == JunctionRunKind.Route && !s.NameWithheld);
        customs.Name.Should().Be(nameof(CustomsTier));

        // The Lane question and its route are asked on the Red track, so they are its steps.
        var onTrack = steps.Where(s => s.Position > customs.Position).ToList();
        onTrack
            .Where(s => s.Kind != JunctionRunKind.Junction)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(
                s =>
                    s.NameWithheld
                    && s.Name == JunctionEventPayload.WithheldName
                    && s.QuestionKey == null
                    && s.Answer == null
                    && s.Confidence == null
                    && s.Decider == null
                    && s.AnswerWithheld
                    && s.TrackPosition != null,
                $"what a withheld track asks and where it goes would give it away. See {Adr}."
            );
        onTrack.Should().OnlyContain(s => s.NameWithheld);

        var rows = await Rows(metadataId);
        rows.Where(r => r.Position > customs.Position)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(r =>
                r.NameWithheld
                && r.Name == JunctionEventPayload.WithheldName
                && r.QuestionKey == null
                && r.Answer == null
                && r.Confidence == null
            );

        var published = Broadcaster
            .For(metadataId)
            .Concat(Handler.For(metadataId))
            .Select(m => JsonSerializer.Serialize(m.Junction))
            .ToList();
        var stored = rows.Select(r => JsonSerializer.Serialize(r)).ToList();
        published.Should().NotBeEmpty();
        stored.Should().NotBeEmpty();
        foreach (var json in published.Concat(stored))
        {
            json.Should().NotContain(nameof(Lane)).And.NotContain(nameof(Lane.Express));
            json.Should().NotContain($"\"{nameof(CustomsTier.Red)}\"");
        }
    }

    [Test]
    public async Task A_sensitive_question_on_an_open_track_keeps_its_key_and_withholds_what_follows()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Ground, 0.9).Choose(CustomsTier.Green, 0.99));

        var (metadataId, _) = await Run<IGroundInspectionTrain>();

        var steps = Handler.For(metadataId).Select(e => e.Junction!).ToList();
        var lane = steps.Single(s => s.Kind == JunctionRunKind.Route && s.Name == nameof(Lane));
        lane.Answer.Should().Be(nameof(Lane.Ground));

        var customs = steps.Single(s =>
            s.Kind == JunctionRunKind.Route && s.Name == nameof(CustomsTier)
        );
        customs.TrackPosition.Should().Be(lane.Position);
        customs.Answer.Should().BeNull();
        customs.AnswerWithheld.Should().BeTrue();

        steps
            .Where(s => s.Position > customs.Position)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(s => s.NameWithheld && s.TrackPosition == customs.Position);
    }

    [Test]
    public async Task Junctions_on_any_decision_track_carry_its_position_and_keep_their_names()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var (metadataId, _) = await Run<ILaneTrain>();

        var steps = Handler.For(metadataId).Select(e => e.Junction!).ToList();
        steps
            .Where(j => j.Name == nameof(Weigh))
            .Should()
            .HaveCount(2, "Weigh started and completed")
            .And.OnlyContain(j => j.TrackPosition == null);
        steps
            .Where(j => j.Name is nameof(Load) or nameof(Stamp))
            .Should()
            .HaveCount(4)
            .And.OnlyContain(j => j.TrackPosition == 2 && !j.NameWithheld);
    }

    [Test]
    public async Task Each_step_carries_the_id_the_trains_graph_draws_for_it()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));
        var graph = GraphOf<ILaneTrain>();

        var (metadataId, _) = await Run<ILaneTrain>();

        var rows = await Rows(metadataId);
        rows.Select(r => (r.Position, r.NodeId))
            .Should()
            .Equal(
                (0, "Weigh#0"),
                (1, "Decide<ChoiceDecision<Lane>>#0"),
                (2, "Switch<Lane>#0"),
                (3, "Switch<Lane>#0/Express/Load#0"),
                (4, "Stamp#0")
            );
        rows.Select(r => r.NodeId!)
            .Should()
            .BeSubsetOf(Ids(graph.Nodes), "a step's id is the one the graph drew for it");

        Handler
            .For(metadataId)
            .Select(e => (e.Junction!.Position, e.Junction.NodeId))
            .Should()
            .OnlyContain(
                e => rows.Single(r => r.Position == e.Position).NodeId == e.NodeId,
                "a junction's start and end name the same node as its row"
            );
    }

    [Test]
    public async Task A_refused_answer_carries_the_id_of_the_question_it_answered()
    {
        Decider.Use(new ScriptedDecider().Answer(nameof(Lane), _ => null));

        var (metadataId, _) = await Run<ILaneTrain>(expectFailure: true);

        var refused = Handler
            .For(metadataId)
            .Should()
            .ContainSingle(e => e.EventType == "DecisionRefused")
            .Subject.Junction!;
        refused.NodeId.Should().Be("Decide<ChoiceDecision<Lane>>#0");
        (await Rows(metadataId))
            .Single(r => r.Position == refused.Position)
            .NodeId.Should()
            .Be(refused.NodeId);
    }

    [Test]
    public async Task Steps_on_a_track_whose_answer_is_withheld_carry_no_node_id()
    {
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        var (metadataId, _) = await Run<ICustomsThenStampTrain>();

        var rows = await Rows(metadataId);
        var route = rows.Single(r => r.Kind == JunctionRunKind.Route);
        rows.Where(r => r.Position <= route.Position)
            .Select(r => r.NodeId)
            .Should()
            .Equal("Weigh#0", "Decide<ChoiceDecision<CustomsTier>>#0", "Switch<CustomsTier>#0");
        rows.Where(r => r.Position > route.Position)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(
                r => r.NameWithheld && r.NodeId == null,
                $"a node id names the track and the junction, so it would give a withheld answer away. See {Adr}."
            );
        Handler
            .For(metadataId)
            .Select(e => e.Junction!)
            .Where(j => j.Position > route.Position)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(j => j.NodeId == null);
        Broadcaster
            .For(metadataId)
            .Select(m => JsonSerializer.Serialize(m.Junction))
            .Should()
            .NotContain(json => json.Contains("/Red/"));
    }

    [Test]
    public async Task A_scoped_handler_is_never_called_from_two_branches_at_once()
    {
        // Each branch has its own scope (Trax.Docs/adr/0045), and a scoped handler resolved from
        // the run's scope was one instance called from both branches at once: one holding a
        // database context threw "a second operation was started", which was swallowed, and the
        // event was lost.
        BranchMeeting.Reset();
        await using var provider = new ServiceCollection()
            .AddScopedTraxRoute<IMeetingTrain, MeetingTrain>()
            .AddScoped<IJunctionEventHandler, OneAtATimeHandler>()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IMeetingTrain>();
        await train.Run(Unit.Default);

        BranchMeeting
            .ConcurrentUse.Should()
            .BeFalse("each branch resolves its handlers from its own scope");
        BranchMeeting
            .Handled.Where(n => n is nameof(MeetA) or nameof(MeetB))
            .Should()
            .HaveCount(4, "every branch junction's start and end reaches a handler");
    }

    [Test]
    public async Task Junction_events_are_off_unless_the_host_asks_for_them()
    {
        var handler = new CapturingHandler();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        await train.Run(JunctionEventTrains.Parcel());

        handler.All.Should().BeEmpty($"junction events are off unless the host asks. See {Adr}.");
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.JunctionRuns.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task A_handler_or_broadcaster_that_throws_does_not_change_the_run()
    {
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler, ThrowingHandler>()
            .AddSingleton<ITrainEventBroadcaster, ThrowingBroadcaster>()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        var output = await train.Run(JunctionEventTrains.Parcel());

        output.Should().Be("loaded|stamped");
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);

        await provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.JunctionRuns.AsNoTracking().ForRun(train.Metadata.Id).CountAsync())
            .Should()
            .Be(5, "storing does not depend on the handlers or the transport");
    }

    [Test]
    public async Task A_host_observer_decision_recording_and_junction_events_are_all_told()
    {
        var observer = new CountingObserver();
        var handler = new CapturingHandler();
        var services = JunctionEventTrains.Register(new ServiceCollection(), Decider);

        // Registered before AddTrax: it used to win, and silently turn decision recording off.
        services.AddSingleton<IDecisionObserver>(observer);

        await using var provider = services
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddJunctionEvents().AddDecisionRecording()
                )
            )
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        await train.Run(JunctionEventTrains.Parcel());

        observer.Decided.Should().Be(1);
        observer.Routed.Should().Be(1);
        handler.For(train.Metadata!.Id).Should().Contain(e => e.EventType == "Decided");
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.RecordedDecisions.CountAsync(d => d.MetadataId == train.Metadata.Id))
            .Should()
            .Be(1, "decision recording is told as well");
    }

    [Test]
    public async Task An_observer_registered_after_AddTrax_refuses_the_host_and_every_recorded_run()
    {
        var observer = new CountingObserver();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddDecisionRecording().AddJunctionEvents()
                )
            )
            .AddSingleton<IDecisionObserver>(observer)
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var gate = provider
            .GetServices<IHostedService>()
            .OfType<DecisionObserverCheck>()
            .Should()
            .ContainSingle()
            .Subject;
        var start = () => gate.StartingAsync(CancellationToken.None);
        await start
            .Should()
            .ThrowAsync<InvalidOperationException>(
                $"a decision would be acted on without being recorded. See {Adr}."
            )
            .WithMessage("*before AddTrax*");

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        var run = async () => await train.Run(JunctionEventTrains.Parcel());
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*before AddTrax*");
        observer.Decided.Should().Be(0, "the run refused before it asked anything");
    }

    [Test]
    public async Task An_observer_registered_after_AddTrax_refuses_a_host_with_junction_events()
    {
        var handler = new CapturingHandler();
        var observer = new CountingObserver();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .AddSingleton<IDecisionObserver>(observer)
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        var gate = provider.GetServices<IHostedService>().OfType<DecisionObserverCheck>().Single();
        var start = () => gate.StartingAsync(CancellationToken.None);
        await start
            .Should()
            .ThrowAsync<InvalidOperationException>(
                $"junction events withhold a track's steps only when told of its routing. See {Adr}."
            )
            .WithMessage("*JunctionEventDecisionObserver*before AddTrax*");

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ICustomsThenStampTrain>();
        var run = async () => await train.Run(JunctionEventTrains.Parcel());
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*before AddTrax*");
        observer.Decided.Should().Be(0, "the run refused before it asked anything");
        handler
            .For(((ServiceTrain<Parcel, string>)(object)train).Metadata!.Id)
            .Should()
            .NotContain(e => e.Junction!.Name == nameof(Load));
    }

    [Test]
    public async Task An_observer_registered_before_AddTrax_leaves_junction_events_withholding()
    {
        var handler = new CapturingHandler();
        var observer = new CountingObserver();
        var services = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IDecisionObserver>(observer)
            .AddSingleton<IJunctionEventHandler>(handler);
        await using var provider = services
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(CustomsTier.Red, 0.99));

        var gate = provider.GetServices<IHostedService>().OfType<DecisionObserverCheck>().Single();
        await gate.Invoking(g => g.StartingAsync(CancellationToken.None)).Should().NotThrowAsync();

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ICustomsThenStampTrain>();
        await train.Run(JunctionEventTrains.Parcel());

        observer.Routed.Should().Be(1);
        var steps = handler
            .For(((ServiceTrain<Parcel, string>)(object)train).Metadata!.Id)
            .Select(e => e.Junction!)
            .ToList();
        steps.Should().Contain(j => j.Kind == JunctionRunKind.Route);
        steps
            .Where(j => j.Kind == JunctionRunKind.Junction && j.TrackPosition != null)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(j => j.NameWithheld);
    }

    [Test]
    public async Task A_best_effort_observer_that_cannot_be_built_is_left_out()
    {
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IDecisionObserver, UnbuildableObserver>()
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddDecisionRecording().AddJunctionEvents()
                )
            )
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var gate = provider.GetServices<IHostedService>().OfType<DecisionObserverCheck>().Single();
        await gate.Invoking(g => g.StartingAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync("an observer that is best effort cannot stop decisions being recorded");

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        (await train.Run(JunctionEventTrains.Parcel())).Should().Be("loaded|stamped");
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        (await context.RecordedDecisions.CountAsync(d => d.MetadataId == train.Metadata!.Id))
            .Should()
            .Be(1);
    }

    [Test]
    public async Task A_required_observer_that_cannot_be_built_refuses_the_host()
    {
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IDecisionObserver, UnbuildableRequiredObserver>()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .BuildServiceProvider();

        var gate = provider.GetServices<IHostedService>().OfType<DecisionObserverCheck>().Single();
        await gate.Invoking(g => g.StartingAsync(CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be built*");
    }

    [Test]
    public async Task A_decision_a_required_observer_could_not_record_is_not_reported_as_made()
    {
        var handler = new CapturingHandler();
        var services = JunctionEventTrains.Register(new ServiceCollection(), Decider);
        services.AddSingleton<IDecisionObserver, FailingRequiredObserver>();

        await using var provider = services
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        var run = async () => await train.Run(JunctionEventTrains.Parcel());

        await run.Should().ThrowAsync<Exception>();
        handler
            .For(train.Metadata!.Id)
            .Should()
            .Contain(
                e => e.Junction!.Name == nameof(Weigh),
                "the junction before the question ran and was published"
            )
            .And.NotContain(e => e.EventType == "Decided" || e.EventType == "Routed");
    }

    [Test]
    public async Task A_manifests_run_carries_its_attempt_counted_from_its_last_success()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));
        var manifestId = await SeedManifestRuns(
            TrainState.Completed,
            TrainState.Failed,
            TrainState.Failed,
            // A dispatch attempt the scheduler requeued is not a run of the job.
            TrainState.Pending
        );

        var metadataId = await RunForManifest(_provider, manifestId);

        Handler
            .For(metadataId)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(
                e => e.Junction!.Attempt == 3,
                $"two failed runs since the last success make this the third attempt. See {Adr}."
            );
        (await Rows(metadataId)).Should().NotBeEmpty().And.OnlyContain(r => r.Attempt == 3);
    }

    [Test]
    public async Task A_run_whose_attempt_cannot_be_read_still_runs_and_carries_none()
    {
        var handler = new CapturingHandler();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .AddSingleton<IRunAttempts, BrokenRunAttempts>()
            .BuildServiceProvider();
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var metadataId = await RunForManifest(provider, manifestId: 424242);

        handler
            .For(metadataId)
            .Should()
            .HaveCount(8, "the run went through every step")
            .And.OnlyContain(e => e.Junction!.Attempt == null);
    }

    [Test]
    public async Task A_long_failure_streak_is_counted_over_a_bounded_number_of_runs()
    {
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));
        var manifestId = await SeedManifestRuns([
            TrainState.Failed,
            TrainState.Cancelled,
            TrainState.Pending,
            .. Enumerable.Repeat(TrainState.Failed, RunAttempts.MaxRunsRead + 500),
        ]);

        var metadataId = await RunForManifest(_provider, manifestId);

        Handler
            .For(metadataId)
            .Should()
            .HaveCount(8, "the run went through every step")
            .And.OnlyContain(
                e => e.Junction!.Attempt == RunAttempts.MaxRunsRead + 1,
                $"the attempt reads at most {RunAttempts.MaxRunsRead} runs. See {Adr}."
            );
    }

    [Test]
    public async Task A_run_does_not_wait_long_on_an_attempt_that_never_comes()
    {
        var handler = new CapturingHandler();
        await using var provider = JunctionEventTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton<IJunctionEventHandler>(handler)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory().AddJunctionEvents()))
            .AddSingleton<IRunAttempts, HangingRunAttempts>()
            .BuildServiceProvider();
        provider.GetRequiredService<JunctionEventPublisher>().AttemptTimeout =
            TimeSpan.FromMilliseconds(50);
        Decider.Use(new ScriptedDecider().Choose(Lane.Express, 0.9));

        var metadataId = await RunForManifest(provider, manifestId: 777)
            .WaitAsync(TimeSpan.FromSeconds(10));

        handler
            .For(metadataId)
            .Should()
            .HaveCount(8)
            .And.OnlyContain(
                e => e.Junction!.Attempt == null,
                $"an attempt that is not read in time is left out. See {Adr}."
            );
    }

    /// <summary>
    /// A manifest with one finished run per state given, oldest first. <see cref="TrainState.Pending"/>
    /// stands for a failed dispatch attempt the scheduler requeued.
    /// </summary>
    private async Task<long> SeedManifestRuns(params TrainState[] runs)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(ILaneTrain) });
        await context.Track(manifest);
        await context.SaveChanges(CancellationToken.None);

        foreach (var state in runs)
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(ILaneTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = JunctionEventTrains.Parcel(),
                    ManifestId = manifest.Id,
                }
            );

            if (state == TrainState.Pending)
            {
                run.TrainState = TrainState.Failed;
                var requeued = new InvalidOperationException("dispatch failed");
                requeued.Data["TrainExceptionData"] = new TrainExceptionData
                {
                    TrainName = run.Name,
                    TrainExternalId = run.ExternalId,
                    Type = "DispatchRequeued",
                    Junction = "Dispatch",
                    Message = "dispatch failed",
                };
                run.AddException(requeued);
            }
            else
            {
                run.TrainState = state;
                if (state == TrainState.Failed)
                    run.AddException(new InvalidOperationException("failed"));
            }

            await context.Track(run);
            await context.SaveChanges(CancellationToken.None);
        }

        return manifest.Id;
    }

    private static async Task<long> RunForManifest(IServiceProvider provider, long manifestId)
    {
        using var scope = provider.CreateScope();
        var train = (LaneTrain)scope.ServiceProvider.GetRequiredService<ILaneTrain>();
        var input = JunctionEventTrains.Parcel();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ILaneTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ManifestId = manifestId,
            }
        );

        (await train.Run(input, metadata)).Should().Be("loaded|stamped");
        await provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return train.Metadata!.Id;
    }

    private async Task<(long MetadataId, string? Output)> Run<TTrain>(bool expectFailure = false)
        where TTrain : class, IServiceTrain<Parcel, string>
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<TTrain>();
        string? output = null;

        try
        {
            output = await train.Run(JunctionEventTrains.Parcel());
            expectFailure.Should().BeFalse("the run completed");
        }
        catch (Exception) when (expectFailure) { }

        var serviceTrain = (ServiceTrain<Parcel, string>)(object)train;
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return (serviceTrain.Metadata!.Id, output);
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync();
    }

    /// <summary>The graph of a fresh instance of the train, as a tool reading it would draw it.</summary>
    private ChainGraph GraphOf<TTrain>()
        where TTrain : class, IServiceTrain<Parcel, string>
    {
        using var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<Parcel, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        return ChainGraph.From(
            train.DeclaredChain(),
            train.GetType(),
            typeof(Parcel),
            typeof(string)
        );
    }

    private static IEnumerable<string> Ids(IEnumerable<ChainGraphNode> nodes) =>
        nodes.SelectMany(n => Ids(n.Tracks.SelectMany(t => t.Nodes)).Prepend(n.Id));
}

internal static class JunctionEventTrains
{
    public const string InputSecret = "INPUT-SECRET-7f3a";
    public const string OutputSecret = "OUTPUT-SECRET-91c2";
    public const string FailureSecret = "FAILURE-SECRET-5d0e";

    public static Parcel Parcel() => new($"p-{Guid.NewGuid():N}", InputSecret);

    public static IServiceCollection Register(IServiceCollection services, IDecider decider) =>
        services
            .AddSingleton(decider)
            .AddScopedTraxRoute<ILaneTrain, LaneTrain>()
            .AddScopedTraxRoute<ILaneThenFailTrain, LaneThenFailTrain>()
            .AddScopedTraxRoute<ICustomsTrain, CustomsTrain>()
            .AddScopedTraxRoute<IHeldTrain, HeldTrain>()
            .AddScopedTraxRoute<IAuditTrain, AuditTrain>()
            .AddScopedTraxRoute<ICustomsThenStampTrain, CustomsThenStampTrain>()
            .AddScopedTraxRoute<IInspectionTrain, InspectionTrain>()
            .AddScopedTraxRoute<IGroundInspectionTrain, GroundInspectionTrain>();
}

/// <summary>Records every junction event it is handed.</summary>
internal sealed class CapturingHandler : IJunctionEventHandler
{
    private readonly ConcurrentQueue<TrainLifecycleEventMessage> _seen = new();

    public IReadOnlyList<TrainLifecycleEventMessage> All => _seen.ToList();

    public List<TrainLifecycleEventMessage> For(long metadataId) =>
        _seen.Where(m => m.MetadataId == metadataId).ToList();

    public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        _seen.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Records every message published to the transport.</summary>
internal sealed class CapturingBroadcaster : ITrainEventBroadcaster
{
    private readonly ConcurrentQueue<TrainLifecycleEventMessage> _seen = new();

    public List<TrainLifecycleEventMessage> For(long metadataId) =>
        _seen.Where(m => m.MetadataId == metadataId).ToList();

    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        _seen.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Never answers, and ignores cancellation, as a stuck connection would.</summary>
internal sealed class HangingRunAttempts : IRunAttempts
{
    private readonly TaskCompletionSource<int?> _never = new();

    public Task<int?> AttemptOf(
        Trax.Effect.Models.Metadata.Metadata metadata,
        CancellationToken cancellationToken
    ) => _never.Task;
}

internal sealed class BrokenRunAttempts : IRunAttempts
{
    public Task<int?> AttemptOf(
        Trax.Effect.Models.Metadata.Metadata metadata,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("the database is down");
}

internal sealed class CapturingJournalLogger : ILogger<DecisionJournal>
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyList<string> Messages => _messages.ToList();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => _messages.Enqueue(formatter(state, exception));
}

/// <summary>
/// What <see cref="OneAtATimeHandler"/> saw: whether any instance was used from two threads at
/// once, and the junctions whose events it handled.
/// </summary>
internal static class BranchMeeting
{
    private static TaskCompletionSource _a = New();
    private static TaskCompletionSource _b = New();

    public static bool ConcurrentUse;
    public static ConcurrentQueue<string> Handled = new();

    public static void Reset()
    {
        _a = New();
        _b = New();
        ConcurrentUse = false;
        Handled = new();
    }

    /// <summary>
    /// Holds a branch junction's start event until the other branch's has arrived too, so the
    /// two are handled at the same time.
    /// </summary>
    public static Task Meet(string junction)
    {
        var (mine, theirs) = junction == nameof(MeetA) ? (_a, _b) : (_b, _a);
        mine.TrySetResult();
        return theirs.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>A scoped handler that, like one holding a database context, must not be used concurrently.</summary>
internal sealed class OneAtATimeHandler : IJunctionEventHandler
{
    private int _inUse;

    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _inUse) > 1)
            BranchMeeting.ConcurrentUse = true;

        try
        {
            var name = message.Junction?.Name;
            if (
                message.EventType == TrainLifecycleEventMessage.JunctionStartedEventType
                && name is nameof(MeetA) or nameof(MeetB)
            )
                await BranchMeeting.Meet(name);

            if (name is not null)
                BranchMeeting.Handled.Enqueue(name);
        }
        finally
        {
            Interlocked.Decrement(ref _inUse);
        }
    }
}

public sealed record MetA;

public sealed record MetB;

public class MeetStart : EffectJunction<Unit, Unit>
{
    public override Task<Unit> Run(Unit input) => Task.FromResult(Unit.Default);
}

public class MeetA : EffectJunction<Unit, MetA>
{
    public override Task<MetA> Run(Unit input) => Task.FromResult(new MetA());
}

public class MeetB : EffectJunction<Unit, MetB>
{
    public override Task<MetB> Run(Unit input) => Task.FromResult(new MetB());
}

public class MeetJoin : EffectJunction<(MetA, MetB), Unit>
{
    public override Task<Unit> Run((MetA, MetB) input) => Task.FromResult(Unit.Default);
}

public interface IMeetingTrain : IServiceTrain<Unit, Unit>;

/// <summary>Two branches whose junctions start at the same time.</summary>
public class MeetingTrain : ServiceTrain<Unit, Unit>, IMeetingTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<MeetStart>()
            .Parallel(p => p.Branch("a", b => b.Chain<MeetA>()).Branch("b", b => b.Chain<MeetB>()))
            .Chain<MeetJoin>()
            .Resolve();
}

internal sealed class ThrowingHandler : IJunctionEventHandler
{
    public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct) =>
        throw new InvalidOperationException("the handler is broken");
}

internal sealed class ThrowingBroadcaster : ITrainEventBroadcaster
{
    public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct) =>
        throw new InvalidOperationException("the broker is down");
}

internal sealed class CountingObserver : IDecisionObserver
{
    private int _decided;
    private int _routed;

    public int Decided => _decided;
    public int Routed => _routed;

    Task IDecisionObserver.Decided(DecisionMade decision, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _decided);
        return Task.CompletedTask;
    }

    Task IDecisionObserver.Routed(TrackRouted routing, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _routed);
        return Task.CompletedTask;
    }
}

/// <summary>A best-effort observer whose constructor throws.</summary>
internal sealed class UnbuildableObserver : IDecisionObserver
{
    public UnbuildableObserver() => throw new InvalidOperationException("misconfigured");

    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>An observer that says whether it is required, and whose constructor throws.</summary>
internal sealed class UnbuildableRequiredObserver : IDecisionObserver
{
    public UnbuildableRequiredObserver() => throw new InvalidOperationException("misconfigured");

    public bool Required => true;

    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>A required observer whose every write fails.</summary>
internal sealed class FailingRequiredObserver : IDecisionObserver
{
    public bool Required => true;

    public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("the journal is down");

    public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("the journal is down");
}

/// <summary>The registered decider, swapped per test.</summary>
internal sealed class SwitchableDecider : IDecider
{
    private IDecider _inner = new ScriptedDecider();

    public void Use(IDecider inner) => _inner = inner;

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
        _inner.Decide(request, ct);
}

internal sealed class WarehouseClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is InvalidOperationException ? FailureClass.Permanent : null;
}

public sealed record Parcel(string Id, [property: TraxSensitive] string Secret);

[Asks("Which lane should this parcel take?")]
public enum Lane
{
    Express,
    Ground,
}

[TraxSensitive]
[Asks("Which customs tier applies to this parcel?")]
public enum CustomsTier
{
    Green,
    Red,
}

public class Weigh : EffectJunction<Parcel, Parcel>
{
    public override Task<Parcel> Run(Parcel input) => Task.FromResult(input);
}

public class Load : EffectJunction<Parcel, string>
{
    public override Task<string> Run(Parcel input) => Task.FromResult("loaded");
}

public class LoadSecret : EffectJunction<Parcel, string>
{
    public override Task<string> Run(Parcel input) =>
        Task.FromResult(JunctionEventTrains.OutputSecret);
}

public class Stamp : EffectJunction<string, string>
{
    public override Task<string> Run(string input) => Task.FromResult($"{input}|stamped");
}

public class Explode : EffectJunction<string, string>
{
    public override Task<string> Run(string input) =>
        throw new InvalidOperationException(JunctionEventTrains.FailureSecret);
}

public class NeverReached : EffectJunction<string, string>
{
    public override Task<string> Run(string input) => Task.FromResult(input);
}

public interface ILaneTrain : IServiceTrain<Parcel, string>;

public class LaneTrain : ServiceTrain<Parcel, string>, ILaneTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<Weigh>()
            .Switch<Parcel, Lane>(tracks =>
                tracks
                    .When(Lane.Express, t => t.Chain<Load>())
                    .When(Lane.Ground, t => t.Chain<Load>())
            )
            .Chain<Stamp>()
            .Resolve();
}

public interface ILaneThenFailTrain : IServiceTrain<Parcel, string>;

/// <summary>Its output carries a secret, and its last junction fails with one in the message.</summary>
public class LaneThenFailTrain : ServiceTrain<Parcel, string>, ILaneThenFailTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<Weigh>()
            .Switch<Parcel, Lane>(tracks =>
                tracks
                    .When(Lane.Express, t => t.Chain<LoadSecret>())
                    .When(Lane.Ground, t => t.Chain<LoadSecret>())
            )
            .Chain<Stamp>()
            .Chain<Explode>()
            .Chain<NeverReached>()
            .Resolve();
}

public interface ICustomsTrain : IServiceTrain<Parcel, string>;

public class CustomsTrain : ServiceTrain<Parcel, string>, ICustomsTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<Parcel, CustomsTier>(tracks =>
                tracks
                    .When(CustomsTier.Green, t => t.Chain<Load>())
                    .When(CustomsTier.Red, t => t.Chain<Load>())
            )
            .Resolve();
}

[TraxSensitive]
[Asks("Should this parcel be held?")]
public sealed class Held<T>;

public sealed class Refund;

[TraxSensitive]
public abstract class SensitiveQuestion;

[Asks("Should this parcel be audited?")]
public sealed class Audit : SensitiveQuestion;

public interface IHeldTrain : IServiceTrain<Parcel, string>;

public class HeldTrain : ServiceTrain<Parcel, string>, IHeldTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Gate<Parcel, Held<Refund>>(g => g.Yes(y => y.Chain<Load>()).No(n => n.Chain<Load>()))
            .Resolve();
}

public interface IAuditTrain : IServiceTrain<Parcel, string>;

public class AuditTrain : ServiceTrain<Parcel, string>, IAuditTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Gate<Parcel, Audit>(g => g.Yes(y => y.Chain<Load>()).No(n => n.Chain<Load>())).Resolve();
}

public interface ICustomsThenStampTrain : IServiceTrain<Parcel, string>;

public class CustomsThenStampTrain : ServiceTrain<Parcel, string>, ICustomsThenStampTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<Weigh>()
            .Switch<Parcel, CustomsTier>(tracks =>
                tracks
                    .When(CustomsTier.Green, t => t.Chain<Load>())
                    .When(CustomsTier.Red, t => t.Chain<Load>())
            )
            .Chain<Stamp>()
            .Resolve();
}

public interface IInspectionTrain : IServiceTrain<Parcel, string>;

/// <summary>Its Red track asks a question of its own, which is not sensitive.</summary>
public class InspectionTrain : ServiceTrain<Parcel, string>, IInspectionTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<Weigh>()
            .Switch<Parcel, CustomsTier>(tracks =>
                tracks
                    .When(CustomsTier.Green, t => t.Chain<Load>())
                    .When(
                        CustomsTier.Red,
                        t =>
                            t.Switch<Parcel, Lane>(lanes =>
                                lanes
                                    .When(Lane.Express, l => l.Chain<Load>())
                                    .When(Lane.Ground, l => l.Chain<Load>())
                            )
                    )
            )
            .Chain<Stamp>()
            .Resolve();
}

public interface IGroundInspectionTrain : IServiceTrain<Parcel, string>;

/// <summary>Its Ground track asks a sensitive question.</summary>
public class GroundInspectionTrain : ServiceTrain<Parcel, string>, IGroundInspectionTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<Weigh>()
            .Switch<Parcel, Lane>(tracks =>
                tracks
                    .When(Lane.Express, t => t.Chain<Load>())
                    .When(
                        Lane.Ground,
                        t =>
                            t.Switch<Parcel, CustomsTier>(tiers =>
                                tiers
                                    .When(CustomsTier.Green, c => c.Chain<Load>())
                                    .When(CustomsTier.Red, c => c.Chain<Load>())
                            )
                    )
            )
            .Chain<Stamp>()
            .Resolve();
}
