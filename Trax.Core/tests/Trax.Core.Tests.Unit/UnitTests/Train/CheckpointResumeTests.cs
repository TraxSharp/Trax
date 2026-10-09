using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;
using static Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// A resumed run: it runs <c>Junctions()</c> again and skips every step before its resume point
/// in place, counting each one so the ids and askings after the point are the original run's, and
/// starts from what the checkpoint restores.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property("adr", CheckpointFixtures.Adr)]
public class CheckpointResumeTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const string Checkpoint = "Checkpoint<Checked>#0";

    [Test]
    public async Task A_run_reaching_a_checkpoint_hands_the_store_its_state_and_routes()
    {
        var store = new Store();

        var result = await new ResearchTrain(
            new Ran(),
            new Crash(),
            With(store, new ScriptedDecider().Choose(Source.Papers))
        ).RunEither("graphs");

        result.IsRight.Should().BeTrue();
        var taken = store.Taken.Should().ContainSingle().Subject;
        taken.NodeId.Should().Be(Checkpoint);
        taken.BranchPath.Should().BeNull();
        taken.StateType.Should().Be(typeof(Checked));
        taken.State.Should().Be(new Checked("graphs", "papers", 12));
        taken
            .Tracks.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new TrackTaken<Source>("Papers", null), $"the route taken before it ({Adr})");
    }

    [Test]
    public async Task A_store_that_fails_fails_the_step()
    {
        var store = new Store { Fails = new InvalidOperationException("over the cap") };

        var result = await new ResearchTrain(
            new Ran(),
            new Crash(),
            With(store, new ScriptedDecider().Choose(Source.Web))
        ).RunEither("graphs");

        result.Swap().ValueUnsafe().Message.Should().Be("over the cap");
    }

    [Test]
    public async Task A_run_without_a_store_takes_no_checkpoint_and_runs_on()
    {
        var result = await new ResearchTrain(
            new Ran(),
            new Crash(),
            new Services().With<IDecider>(new ScriptedDecider().Choose(Source.Web))
        ).RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs from web, 12 pages"));
    }

    [Test]
    public async Task Steps_before_the_resume_point_run_nothing_and_ask_no_decider()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        var first = await new ResearchTrain(
            new Ran(),
            crash,
            With(store, new ScriptedDecider().Choose(Source.Papers))
        ).RunEither("graphs");
        first.IsLeft.Should().BeTrue("the summariser crashed");

        crash.Failing = false;
        var ran = new Ran();
        var unasked = new ScriptedDecider();
        var resumed = new ResearchTrain(ran, crash, With(new Store(), unasked));
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs from papers, 12 pages"));
        ran.Junctions.Should()
            .Equal(["Summarize"], $"steps before the checkpoint are skipped ({Adr})");
        unasked.Requests.Should().BeEmpty("a skipped question is not asked");
    }

    [Test]
    public async Task Node_ids_and_asking_occurrences_after_the_point_equal_the_original_runs()
    {
        var store = new Store();
        var replay = new NotingReplay();
        var ran = new Ran();
        var decider = new ScriptedDecider().Choose(Source.Papers);
        var crash = new Crash { Failing = true };

        await new AsksTwiceTrain(
            ran,
            crash,
            With(store, decider).With<IDecisionReplay>(replay)
        ).RunEither("graphs");

        crash.Failing = false;
        var resumedRan = new Ran();
        var resumedReplay = new NotingReplay();
        var resumed = new AsksTwiceTrain(
            resumedRan,
            crash,
            With(new Store(), decider).With<IDecisionReplay>(resumedReplay)
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        (await resumed.RunEither("graphs")).IsRight.Should().BeTrue();

        resumedReplay
            .Asked.Should()
            .Equal(
                [replay.Asked[1]],
                $"the question asked after the checkpoint keeps the occurrence it had ({Adr})"
            );
        resumedRan
            .Nodes.Should()
            .Equal(
                ran.Nodes.Skip(ran.Junctions.IndexOf("SearchPapers", 2)),
                "every step after the point runs as the node it ran as"
            );
    }

    [Test]
    public async Task Questions_after_the_point_are_asked_afresh_when_nothing_replays_them()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        await new AsksTwiceTrain(
            new Ran(),
            crash,
            With(store, new ScriptedDecider().Choose(Source.Papers))
        ).RunEither("graphs");

        crash.Failing = false;
        var ran = new Ran();
        var decider = new ScriptedDecider().Choose(Source.Web);
        var resumed = new AsksTwiceTrain(ran, crash, With(new Store(), decider));
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs from web, 12 pages"));
        decider
            .Requests.Should()
            .ContainSingle("only the question after the checkpoint is asked, and asked afresh");
        ran.Junctions.Should().Equal(["SearchWeb", "FetchFullTexts", "Summarize"], Adr);
    }

    [Test]
    public async Task A_value_handed_to_AddServices_is_handed_again_on_resume()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        await new TaggedTrain(
            new Tag("first"),
            crash,
            new Services().With<ICheckpointStore>(store)
        ).RunEither("graphs");

        crash.Failing = false;
        var resumed = new TaggedTrain(
            new Tag("second"),
            crash,
            new Services().With<ICheckpointStore>(new Store())
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result
            .ValueUnsafe()
            .Should()
            .Be(
                new Report("graphs, tagged second"),
                $"Junctions() runs again, so a value handed to it is the resumed run's own ({Adr})"
            );
    }

    [Test]
    public async Task A_routing_step_before_the_point_takes_the_stored_track()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        await new CheckpointInATrackTrain(
            new Ran(),
            crash,
            With(store, new ScriptedDecider().Choose(Source.Papers))
        ).RunEither("graphs");

        store.Taken.Single().NodeId.Should().Be($"Switch<Source>#0/Papers/{Checkpoint}");

        crash.Failing = false;
        var ran = new Ran();
        var unasked = new ScriptedDecider();
        var resumed = new CheckpointInATrackTrain(ran, crash, With(new Store(), unasked));
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs from papers, 12 pages"));
        ran.Junctions.Should().Equal(["Summarize"], $"the run goes down the stored track ({Adr})");
        unasked.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task An_operator_resume_at_a_later_step_skips_to_it()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        await new ResearchTrain(
            new Ran(),
            crash,
            With(store, new ScriptedDecider().Choose(Source.Web))
        ).RunEither("graphs");

        crash.Failing = false;
        var ran = new Ran();
        var resumed = new ResearchTrain(ran, crash, With(new Store(), new ScriptedDecider()));
        resumed.Resume = PlanFor(resumed, store, resumeAt: "Summarize#0");

        (await resumed.RunEither("graphs")).IsRight.Should().BeTrue();
        ran.Junctions.Should().Equal(["Summarize"]);
    }

    [Test]
    public async Task A_resume_point_off_the_path_this_run_takes_fails_the_run()
    {
        var store = new Store();
        await new CheckpointInATrackTrain(
            new Ran(),
            new Crash(),
            With(store, new ScriptedDecider().Choose(Source.Papers))
        ).RunEither("graphs");

        var resumed = new CheckpointInATrackTrain(
            new Ran(),
            new Crash(),
            With(new Store(), new ScriptedDecider())
        )
        {
            Resume = new ResumePlan(
                "Summarize#7",
                false,
                new Dictionary<string, RestoredCheckpoint>()
            ),
        };

        var result = await resumed.RunEither("graphs");

        result
            .Swap()
            .ValueUnsafe()
            .Message.Should()
            .Contain("without reaching its resume point 'Summarize#7'", Adr);
    }

    [Test]
    public async Task A_branch_whose_last_step_is_a_reached_checkpoint_runs_nothing_and_gives_the_join_its_state()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        await new BranchesTrain(new Ran(), crash, With(store, new ScriptedDecider())).RunEither(
            "graphs"
        );

        store
            .Taken.Select(t => t.NodeId)
            .Should()
            .BeEquivalentTo([
                "Checkpoint<Brief>#0",
                $"Parallel#0/web/{Checkpoint}",
                "Parallel#0/score/Checkpoint<Score>#0",
            ]);

        crash.Failing = false;
        var ran = new Ran();
        var resumed = new BranchesTrain(ran, crash, With(new Store(), new ScriptedDecider()));
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs from web, 12 pages, weighed 6"));
        ran.Junctions.Should()
            .Equal(
                ["Weigh", "Combine"],
                $"the web branch ended in a checkpoint it reached, so it runs nothing ({Adr})"
            );
    }

    [Test]
    public async Task Resuming_a_failed_Parallel_reruns_only_the_failed_branch_from_its_checkpoint()
    {
        var store = new Store();
        var crash = new Crash();
        // A failing branch cancels its sibling, so the web branch must have stored its checkpoint
        // before weighing fails, or the resume finds none for it and reruns it.
        var weigh = new Crash
        {
            Failing = true,
            FailsAfter = store.Written("Parallel#0/web/Checkpoint<Checked>#0"),
        };
        var first = await new BranchesTrain(
            new Ran(),
            crash,
            With(store, new ScriptedDecider()),
            weigh
        ).RunEither("graphs");
        first.Swap().ValueUnsafe().Should().BeOfType<BranchesFailedException>();

        weigh.Failing = false;
        var ran = new Ran();
        var resumed = new BranchesTrain(
            ran,
            crash,
            With(new Store(), new ScriptedDecider()),
            weigh
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: "Parallel#0");

        (await resumed.RunEither("graphs")).IsRight.Should().BeTrue();
        ran.Junctions.Should()
            .Equal(
                ["Weigh", "Combine"],
                $"only the failed branch runs, from its own checkpoint ({Adr})"
            );
    }

    [Test]
    public async Task Resuming_at_a_step_before_a_Parallel_reruns_its_branches_rather_than_restoring_them()
    {
        var (store, crash, round) = await RefinedRunThatFailedAtCombine();

        var ran = new Ran();
        var resumed = new RefinedBranchesTrain(
            ran,
            crash,
            round,
            With(new Store(), new ScriptedDecider())
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: "Refine#0");

        var result = await resumed.RunEither("graphs");

        result
            .ValueUnsafe()
            .Should()
            .Be(
                new Report("graphs-v2 from web, 12 pages, weighed 9"),
                $"Refine ran again, so each branch's checkpoint holds work from the old brief ({Adr})"
            );
        ran.Junctions.Should()
            .BeEquivalentTo([
                "Refine",
                "SearchWeb",
                "FetchFullTexts",
                "ScoreBrief",
                "Weigh",
                "Combine",
            ]);
    }

    [Test]
    public async Task Resuming_after_a_checkpoint_with_a_step_before_the_Parallel_reruns_its_branches()
    {
        var (store, crash, round) = await RefinedRunThatFailedAtCombine();

        var ran = new Ran();
        var resumed = new RefinedBranchesTrain(
            ran,
            crash,
            round,
            With(new Store(), new ScriptedDecider())
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: null);

        var result = await resumed.RunEither("graphs");

        result.ValueUnsafe().Should().Be(new Report("graphs-v2 from web, 12 pages, weighed 9"));
        ran.Junctions.Should()
            .BeEquivalentTo(
                ["Refine", "SearchWeb", "FetchFullTexts", "ScoreBrief", "Weigh", "Combine"],
                $"Refine runs again after the checkpoint, so the branches cannot restore ({Adr})"
            );
    }

    [Test]
    public async Task Resuming_at_the_Parallel_itself_restores_its_branches()
    {
        var (store, crash, round) = await RefinedRunThatFailedAtCombine();

        var ran = new Ran();
        var resumed = new RefinedBranchesTrain(
            ran,
            crash,
            round,
            With(new Store(), new ScriptedDecider())
        );
        resumed.Resume = PlanFor(resumed, store, resumeAt: "Parallel#0");

        var result = await resumed.RunEither("graphs");

        result
            .ValueUnsafe()
            .Should()
            .Be(
                new Report("graphs-v1 from web, 12 pages, weighed 9"),
                "nothing before the Parallel ran again, so its branches' checkpoints still hold"
            );
        ran.Junctions.Should().Equal(["Weigh", "Combine"]);
    }

    /// <summary>A run of <see cref="RefinedBranchesTrain"/> that failed at Combine, with the next round armed.</summary>
    private static async Task<(Store, Crash, Round)> RefinedRunThatFailedAtCombine()
    {
        var store = new Store();
        var crash = new Crash { Failing = true };
        var round = new Round { Value = 1 };
        var first = await new RefinedBranchesTrain(
            new Ran(),
            crash,
            round,
            With(store, new ScriptedDecider())
        ).RunEither("graphs");
        first.IsLeft.Should().BeTrue();

        crash.Failing = false;
        round.Value = 2;
        return (store, crash, round);
    }

    /// <summary>The plan a host builds: the check's outcome, with the stored states read back.</summary>
    internal static ResumePlan PlanFor<TIn, TOut>(
        Train<TIn, TOut> train,
        Store store,
        string? resumeAt
    )
    {
        var outcome = ChainVerification.CheckResume(
            train.DeclaredChain(),
            typeof(TIn),
            typeof(TOut),
            t => t.IsInterface,
            store.Taken.Select(t => t.NodeId).ToList(),
            resumeAt
        );

        outcome.Refusal.Should().BeNull();

        var restored = new Dictionary<string, RestoredCheckpoint>();
        if (outcome.MainCheckpoint is { } main)
            restored[""] = store.Restored(main);
        foreach (var (branch, node) in outcome.BranchCheckpoints)
            restored[branch] = store.Restored(node);

        return new ResumePlan(outcome.Target, outcome.Inclusive, restored);
    }

    public sealed record Score(int Value);

    public sealed record Weighed(int Value);

    private sealed class ScoreBrief(Ran ran) : Junction<Brief, Score>
    {
        public override Task<Score> Run(Brief input)
        {
            ran.Note(nameof(ScoreBrief));
            return Task.FromResult(new Score(input.Topic.Length));
        }
    }

    private sealed class Weigh(Ran ran, Crash weigh) : Junction<Score, Weighed>
    {
        public override async Task<Weighed> Run(Score input)
        {
            ran.Note(nameof(Weigh));

            if (weigh.Failing)
            {
                if (weigh.FailsAfter is { } after)
                    await after.WaitAsync(TimeSpan.FromSeconds(30));
                throw new TimeoutException("weighing timed out");
            }

            return new Weighed(input.Value);
        }
    }

    private sealed class Combine(Ran ran, Crash crash) : Junction<(Checked, Weighed), Report>
    {
        public override Task<Report> Run((Checked, Weighed) input)
        {
            ran.Note(nameof(Combine));

            if (crash.Failing)
                throw new TimeoutException("combining timed out");

            var (found, weighed) = input;
            return Task.FromResult(
                new Report(
                    $"{found.Topic} from {found.Source}, {found.Pages} pages, weighed {weighed.Value}"
                )
            );
        }
    }

    /// <summary>
    /// <c>PlanResearch → Checkpoint&lt;Brief&gt; → Parallel(web: search, fetch, checkpoint |
    /// score: score, checkpoint, weigh) → Combine</c>.
    /// </summary>
    private sealed class BranchesTrain(Ran ran, Crash crash, Services services, Crash? weigh = null)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(services.With(ran).With(crash))
                .Chain<PlanResearch>()
                .Checkpoint<Brief>()
                .Parallel(p =>
                    p.Branch(
                            "web",
                            b => b.Chain<SearchWeb>().Chain<FetchFullTexts>().Checkpoint<Checked>()
                        )
                        .Branch(
                            "score",
                            b =>
                                b.Chain<ScoreBrief>()
                                    .Checkpoint<Score>()
                                    .Chain(new Weigh(ran, weigh ?? new Crash()))
                        )
                )
                .Chain<Combine>()
                .Resolve();
    }

    public sealed class Round
    {
        public int Value { get; set; }
    }

    /// <summary>Rewrites the brief, a little differently each round.</summary>
    private sealed class Refine(Ran ran, Round round) : Junction<Brief, Brief>
    {
        public override Task<Brief> Run(Brief input)
        {
            ran.Note(nameof(Refine));
            return Task.FromResult(new Brief($"{input.Topic}-v{round.Value}"));
        }
    }

    /// <summary>
    /// <c>PlanResearch → Checkpoint&lt;Brief&gt; → Refine → Parallel(web: search, fetch,
    /// checkpoint | score: score, checkpoint, weigh) → Combine</c>.
    /// </summary>
    private sealed class RefinedBranchesTrain(Ran ran, Crash crash, Round round, Services services)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(services.With(ran).With(crash))
                .Chain<PlanResearch>()
                .Checkpoint<Brief>()
                .Chain(new Refine(ran, round))
                .Parallel(p =>
                    p.Branch(
                            "web",
                            b => b.Chain<SearchWeb>().Chain<FetchFullTexts>().Checkpoint<Checked>()
                        )
                        .Branch(
                            "score",
                            b =>
                                b.Chain<ScoreBrief>()
                                    .Checkpoint<Score>()
                                    .Chain(new Weigh(ran, new Crash()))
                        )
                )
                .Chain<Combine>()
                .Resolve();
    }

    public interface ITag
    {
        string Name { get; }
    }

    public sealed record Tag(string Name) : ITag;

    private sealed class TagReport(ITag tag, Crash crash) : Junction<Brief, Report>
    {
        public override Task<Report> Run(Brief input)
        {
            if (crash.Failing)
                throw new TimeoutException("tagging timed out");

            return Task.FromResult(new Report($"{input.Topic}, tagged {tag.Name}"));
        }
    }

    /// <summary>A value handed to AddServices before a checkpoint, read by a step after it.</summary>
    private sealed class TaggedTrain(ITag tag, Crash crash, Services services)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider, ITag>(services.With(new Ran()).With(crash), tag)
                .Chain<PlanResearch>()
                .Checkpoint<Brief>()
                .Chain<TagReport>()
                .Resolve();
    }

    /// <summary>Asks which source before the checkpoint and again after it.</summary>
    private sealed class AsksTwiceTrain(Ran ran, Crash crash, Services services)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(services.With(ran).With(crash))
                .Chain<PlanResearch>()
                .Switch<Brief, Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>())
                        .When(Source.Papers, p => p.Chain<SearchPapers>())
                )
                .Checkpoint<Brief>()
                .Switch<Brief, Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>())
                        .When(Source.Papers, p => p.Chain<SearchPapers>())
                )
                .Chain<FetchFullTexts>()
                .Chain<Summarize>()
                .Resolve();
    }
}
