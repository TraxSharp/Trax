using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;
using static Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Whether a run can resume at a step, decided over the declared chain before anything runs: a
/// forward walk from the point, in which every step must find its inputs in what the checkpoint
/// restores, the train's input, the container, or a step after the point.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property("adr", CheckpointFixtures.Adr)]
public class ResumeCheckTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const string Checkpoint = "Checkpoint<Checked>#0";

    [Test]
    public void After_the_latest_checkpoint_is_the_default_point()
    {
        var outcome = Check(
            new ResearchTrain(new Ran(), new Crash(), new Services()),
            [Checkpoint]
        );

        outcome.CanResume.Should().BeTrue();
        outcome.Target.Should().Be(Checkpoint);
        outcome.Inclusive.Should().BeTrue("the checkpoint is restored rather than run");
        outcome.MainCheckpoint.Should().Be(Checkpoint);
        outcome.StateTypes[Checkpoint].Should().Be(typeof(Checked));
        outcome
            .TrackTypes[Checkpoint]
            .Should()
            .Equal([typeof(TrackTaken<Source>)], "the route taken before it is restored with it");
    }

    [Test]
    public void A_type_produced_after_the_step_that_needs_it_is_refused()
    {
        // FetchFullTexts needs Findings, which only Refind, after it, produces once the resume has
        // skipped SearchWeb. Taking what the steps after the point produce as available would let
        // it through.
        var outcome = Check(new RefindsTrain(), ["Checkpoint<Brief>#0"]);

        outcome.RefusalCode.Should().Be(ResumeRefusals.MissingInput, Adr);
        outcome
            .Refusal.Should()
            .Contain(
                "'FetchFullTexts#0' needs 'Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures+Findings'"
            )
            .And.Contain("no checkpoint before the resume point holds one");
    }

    [Test]
    public void A_value_a_skipped_step_overwrote_is_refused_as_stale()
    {
        // Found by the property test: PlanResearch rewrites the train's input, a string, before
        // the checkpoint, and Summarize reads it after. A resumed run still holds the input, so
        // it would run, on the value the original run had replaced.
        var outcome = Check(new RewritesItsInputTrain(), ["Checkpoint<Checked>#0"]);

        outcome.RefusalCode.Should().Be(ResumeRefusals.StaleValue, Adr);
        outcome
            .Refusal.Should()
            .Contain("'Echo#0' reads 'System.String'")
            .And.Contain("older value");
    }

    [Test]
    public void A_step_reading_a_decision_from_before_the_checkpoint_is_refused_naming_it()
    {
        var outcome = Check(new DecidesBeforeTrain(), ["Checkpoint<Brief>#0"]);

        outcome.RefusalCode.Should().Be(ResumeRefusals.MissingInput, Adr);
        outcome.Refusal.Should().Contain("ChoiceDecision", "decisions are not stored");
    }

    [Test]
    public void A_join_missing_a_skipped_branchs_output_is_refused()
    {
        var outcome = Check(
            new BranchKeepsTooLittleTrain(),
            ["Parallel#0/web/Checkpoint<Findings>#0"]
        );

        outcome.RefusalCode.Should().Be(ResumeRefusals.MissingInput, Adr);
        outcome
            .Refusal.Should()
            .Contain("Checked", "the skipped branch gives the join only what its checkpoint holds");
    }

    [Test]
    public void A_chain_hash_or_state_fingerprint_mismatch_is_refused_with_its_own_reason()
    {
        var fingerprint = CheckpointState.Fingerprint(typeof(Checked));

        ChainVerification
            .CheckStored(Checkpoint, "a", "b", fingerprint, typeof(Checked))!
            .RefusalCode.Should()
            .Be(ResumeRefusals.ChainChanged, Adr);
        ChainVerification
            .CheckStored(Checkpoint, "a", "a", "old", typeof(Checked))!
            .RefusalCode.Should()
            .Be(ResumeRefusals.StateChanged, Adr);
        ChainVerification
            .CheckStored(Checkpoint, "a", "a", fingerprint, typeof(Checked))
            .Should()
            .BeNull();
        CheckpointState
            .Fingerprint(typeof(Checked))
            .Should()
            .NotBe(
                CheckpointState.Fingerprint(typeof(Findings)),
                "a different shape fingerprints differently"
            );
    }

    [Test]
    public void A_run_without_a_checkpoint_is_refused()
    {
        Check(new ResearchTrain(new Ran(), new Crash(), new Services()), [])
            .RefusalCode.Should()
            .Be(ResumeRefusals.NoCheckpoint);
        Check(
            new ResearchTrain(new Ran(), new Crash(), new Services()),
            [Checkpoint],
            "FetchFullTexts#0"
        )
            .RefusalCode.Should()
            .Be(ResumeRefusals.NoCheckpoint, "no checkpoint comes before the point");
    }

    [Test]
    public void A_point_the_chain_does_not_declare_or_cannot_start_at_is_refused()
    {
        var train = new ResearchTrain(new Ran(), new Crash(), new Services());

        Check(train, [Checkpoint], "Nowhere#0").RefusalCode.Should().Be(ResumeRefusals.UnknownStep);
        Check(train, [Checkpoint], "Resolve#0").RefusalCode.Should().Be(ResumeRefusals.NotAStep);
    }

    [Test]
    public void A_point_inside_a_branch_is_refused()
    {
        Check(new BranchKeepsTooLittleTrain(), [], "Parallel#0/web/SearchWeb#0")
            .RefusalCode.Should()
            .Be(ResumeRefusals.InsideABranch);
    }

    [Test]
    public void A_point_in_a_track_after_the_checkpoint_is_refused()
    {
        Check(
            new AfterTheSwitchTrain(),
            ["Checkpoint<Brief>#0"],
            "Switch<Source>#0/Papers/SearchPapers#0"
        )
            .RefusalCode.Should()
            .Be(
                ResumeRefusals.OffThePath,
                "the resumed run would skip the routing step's question"
            );
    }

    [Test]
    public void A_point_inside_the_track_holding_the_checkpoint_is_allowed()
    {
        var outcome = Check(
            new CheckpointInATrackTrain(new Ran(), new Crash(), new Services()),
            [$"Switch<Source>#0/Papers/{Checkpoint}"],
            "Summarize#0"
        );

        outcome.Refusal.Should().BeNull();
    }

    private static ResumeOutcome Check<TIn, TOut>(
        Train<TIn, TOut> train,
        IReadOnlyCollection<string> written,
        string? resumeAt = null
    ) =>
        ChainVerification.CheckResume(
            train.DeclaredChain(),
            typeof(TIn),
            typeof(TOut),
            t => t.IsInterface,
            written,
            resumeAt
        );

    private sealed class Refind : Junction<Checked, Findings>
    {
        public override Task<Findings> Run(Checked input) =>
            Task.FromResult(new Findings(input.Topic, input.Source));
    }

    private sealed class Sum : Junction<Findings, Report>
    {
        public override Task<Report> Run(Findings input) =>
            Task.FromResult(new Report(input.Topic));
    }

    private sealed class Rewrite : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input.ToUpperInvariant());
    }

    private sealed class Echo : Junction<string, Report>
    {
        public override Task<Report> Run(string input) => Task.FromResult(new Report(input));
    }

    private sealed class RewritesItsInputTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(new Services().With(new Ran()))
                .Chain<Rewrite>()
                .Chain<PlanResearch>()
                .Chain<SearchWeb>()
                .Chain<FetchFullTexts>()
                .Checkpoint<Checked>()
                .Chain<Echo>()
                .Resolve();
    }

    private sealed class RefindsTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(new Services().With(new Ran()))
                .Chain<PlanResearch>()
                .Chain<SearchWeb>()
                .Checkpoint<Brief>()
                .Chain<FetchFullTexts>()
                .Chain<Refind>()
                .Chain<Sum>()
                .Resolve();
    }

    private sealed class DecidesBeforeTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(new Services().With(new Ran()).With(new Crash()))
                .Chain<PlanResearch>()
                .Decide<Brief>(q => q.Choice<Source>())
                .Checkpoint<Brief>()
                .Switch<Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>())
                        .When(Source.Papers, p => p.Chain<SearchPapers>())
                )
                .Chain<FetchFullTexts>()
                .Chain<Summarize>()
                .Resolve();
    }

    private sealed class AfterTheSwitchTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(new Services().With(new Ran()).With(new Crash()))
                .Chain<PlanResearch>()
                .Checkpoint<Brief>()
                .Switch<Brief, Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>())
                        .When(Source.Papers, p => p.Chain<SearchPapers>())
                )
                .Chain<FetchFullTexts>()
                .Chain<Summarize>()
                .Resolve();
    }

    private sealed class BranchKeepsTooLittleTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(new Services().With(new Ran()).With(new Crash()))
                .Chain<PlanResearch>()
                .Parallel(p =>
                    p.Branch(
                        "web",
                        b => b.Chain<SearchWeb>().Chain<FetchFullTexts>().Checkpoint<Findings>()
                    )
                )
                .Chain<Summarize>()
                .Resolve();
    }
}
