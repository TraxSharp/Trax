using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;
using static Trax.Core.Tests.Unit.UnitTests.Train.CheckpointFixtures;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// What a chain may declare as a checkpoint: a state some earlier step puts in Memory, of a type
/// that reads back as the same value, and never after a short circuit.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[Property("adr", CheckpointFixtures.Adr)]
public class CheckpointDeclarationTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    [Test]
    public void A_state_that_is_not_sealed_is_refused_naming_the_type()
    {
        new CheckpointOf<Open>()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(r => r.Contains("Checkpoint<Open>") && r.Contains("not sealed"), Adr);
    }

    [Test]
    public void A_state_with_an_object_or_interface_member_is_refused_naming_the_member()
    {
        var refusals = new CheckpointOf<Loose>().DeclaredChain().Refusals;

        refusals.Should().Contain(r => r.Contains("'Loose.Anything'") && r.Contains("object"), Adr);
        refusals
            .Should()
            .Contain(r => r.Contains("'Loose.Comparer'") && r.Contains("interface"), Adr);
        refusals
            .Should()
            .NotContain(
                r => r.Contains("'Loose.Names'"),
                "a framework collection interface reads back from its elements"
            );
    }

    [Test]
    public void A_state_with_a_field_the_serializer_skips_is_refused_naming_the_field()
    {
        new CheckpointOf<WithField>()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(r => r.Contains("field 'Count'"), Adr);
    }

    [Test]
    public void A_state_no_earlier_step_puts_in_Memory_is_refused()
    {
        var train = new CheckpointBeforeItsStateTrain();

        ChainVerification
            .Verify(train.DeclaredChain(), typeof(string), typeof(Report))
            .Should()
            .Contain(
                f => f.Kind == ChainStepKind.Checkpoint && f.Reason.Contains("checkpoints"),
                Adr
            );
    }

    [Test]
    public void A_checkpoint_after_a_ShortCircuit_on_its_path_is_refused()
    {
        new AfterShortCircuitTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(r => r.Contains("after a ShortCircuit"), Adr);
    }

    [Test]
    public void A_sealed_state_of_data_is_not_refused()
    {
        new CheckpointOf<Rich>().DeclaredChain().Refusals.Should().BeEmpty();
        new ResearchTrain(new Ran(), new Crash(), new Services())
            .DeclaredChain()
            .Refusals.Should()
            .BeEmpty();
    }

    [Test]
    public void A_checkpoint_exports_as_its_own_node_kind_with_its_state_type()
    {
        var train = new ResearchTrain(new Ran(), new Crash(), new Services());
        var graph = ChainGraph.From(
            train.DeclaredChain(),
            typeof(ResearchTrain),
            typeof(string),
            typeof(Report)
        );

        graph
            .Nodes.Should()
            .ContainSingle(n => n.Kind == ChainStepKind.Checkpoint)
            .Which.Should()
            .Match<ChainGraphNode>(n =>
                n.Id == "Checkpoint<Checked>#0" && n.In == "Checked" && n.Out == null
            );
        graph.ToJson().Should().Contain("\"kind\":\"Checkpoint\"");
    }

    public record Open(string Name);

    public sealed record Loose(object Anything, IComparable Comparer, IReadOnlyList<string> Names);

    public sealed class WithField
    {
        public int Count;

        public string Name { get; set; } = "";
    }

    private sealed class MakesIt<T> : Junction<string, T>
        where T : class
    {
        public override Task<T> Run(string input) => Task.FromResult<T>(null!);
    }

    private sealed class Ends : Junction<string, Report>
    {
        public override Task<Report> Run(string input) => Task.FromResult(new Report(input));
    }

    private sealed class CheckpointOf<T> : Train<string, Report>
        where T : class
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            Chain<MakesIt<T>>().Checkpoint<T>().Chain<Ends>().Resolve();
    }

    private sealed class CheckpointBeforeItsStateTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            Checkpoint<Checked>().Chain<Ends>().Resolve();
    }

    private sealed class ShortCircuitsToReport : Junction<string, Report>
    {
        public override Task<Report> Run(string input) => Task.FromResult(new Report(input));
    }

    private sealed class AfterShortCircuitTrain : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            ShortCircuit<ShortCircuitsToReport>()
                .Chain<MakesIt<Checked>>()
                .Checkpoint<Checked>()
                .Chain<Ends>()
                .Resolve();
    }
}
