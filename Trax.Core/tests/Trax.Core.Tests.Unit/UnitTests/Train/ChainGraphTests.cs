using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// A train's declared chain as a graph: its nodes, their ids, the canonical JSON and its hash, and
/// the id a running step reports, which must be the id the graph drew for it.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md:
/// the graph is read from the declaration, so nothing runs to draw it.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ChainGraphTests : TestSetup
{
    [Test]
    public void From_DrawsEveryStepInOrder_WithItsTypes()
    {
        var graph = GraphOf(new LinearTrain());

        graph.Train.Should().Be(typeof(LinearTrain).FullName);
        graph.Input.Should().Be("String");
        graph.Output.Should().Be("Boolean");
        graph
            .Nodes.Select(n => (n.Id, n.Kind, n.Junction, n.In, n.Out))
            .Should()
            .Equal(
                ("Length#0", ChainStepKind.Chain, "Length", "String", "Int32"),
                ("Positive#0", ChainStepKind.Chain, "Positive", "Int32", "Boolean"),
                ("Resolve#0", ChainStepKind.Resolve, null, null, "Boolean")
            );
        graph.Refusals.Should().BeEmpty();
    }

    [Test]
    public void From_NumbersARepeatedJunctionByItsOccurrence()
    {
        GraphOf(new RepeatingTrain())
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal("Length#0", "Describe#0", "Length#1", "Positive#0", "Resolve#0");
    }

    [Test]
    public void From_GivesTheSameJsonAndHash_OnEveryRead()
    {
        var first = GraphOf(new DecidingTrain(new Capture()));
        var second = GraphOf(new DecidingTrain(new Capture()));

        second.ToJson().Should().Be(first.ToJson());
        second.Hash.Should().Be(first.Hash).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public void Hash_ChangesWhenAStepIsAdded_ButTheOtherIdsDoNot()
    {
        var linear = GraphOf(new LinearTrain());
        var longer = GraphOf(new LongerTrain());

        longer.Hash.Should().NotBe(linear.Hash);
        longer
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal(
                ["Describe#0", "Length#0", "Positive#0", "Resolve#0"],
                "an id names its step, so inserting another step moves no id"
            );
    }

    [Test]
    public void From_DrawsARoutingStepsTracks_WithIdsInsideTheTrack()
    {
        var graph = GraphOf(new DecidingTrain(new Capture()));

        graph
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal(
                "Seed<IDecider>#0",
                "Seed<IDecisionObserver>#0",
                "Decide<ChoiceDecision<Lane>>#0",
                "Switch<Lane>#0",
                "Resolve#0"
            );

        var routing = graph.Nodes[3];
        routing.Kind.Should().Be(ChainStepKind.Switch);
        routing.Tracks.Select(t => t.Name).Should().Equal("Left", "Right");
        routing
            .Tracks[0]
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal("Switch<Lane>#0/Left/Note#0", "Switch<Lane>#0/Left/Positive#0");
        routing
            .Tracks[1]
            .Nodes.Select(n => n.Id)
            .Should()
            .Equal("Switch<Lane>#0/Right/Note#0", "Switch<Lane>#0/Right/Positive#0");
    }

    [Test]
    public void From_MarksAJunctionNamedByItsInterfaceAsOpaque()
    {
        var graph = GraphOf(new InterfaceTrain());

        graph.Nodes[1].Kind.Should().Be(ChainStepKind.IChain);
        graph
            .Nodes[1]
            .Opaque.Should()
            .BeTrue("the junction behind an interface is chosen at run time");
        graph.Nodes[2].Opaque.Should().BeFalse();
    }

    [Test]
    public void From_DrawsExtractAndShortCircuit()
    {
        GraphOf(new ExtractingTrain())
            .Nodes.Select(n => (n.Id, n.Kind))
            .Should()
            .Equal(
                ("Length#0", ChainStepKind.Chain),
                ("Extract<Int32, Int64>#0", ChainStepKind.Extract),
                ("MaybePositive#0", ChainStepKind.ShortCircuit),
                ("Resolve#0", ChainStepKind.Resolve)
            );
    }

    [Test]
    public void ToJson_WritesAbsentValuesAsNull_AndNoWhitespace()
    {
        var json = GraphOf(new LinearTrain()).ToJson();

        json.Should()
            .StartWith($"{{\"train\":\"{typeof(LinearTrain).FullName}\",\"input\":\"String\"");
        json.Should()
            .Contain(
                "{\"id\":\"Resolve#0\",\"kind\":\"Resolve\",\"junction\":null,\"in\":null,"
                    + "\"out\":\"Boolean\",\"opaque\":false,\"tracks\":[]}"
            );
        json.Should().NotContain(" \"").And.NotContain("\n");
    }

    [Test]
    public async Task CurrentNodeId_IsTheGraphsIdForEachRunningStep()
    {
        var capture = new Capture();
        var train = new DecidingTrain(capture);
        var graph = GraphOf(new DecidingTrain(new Capture()));

        var result = await train.RunEither("abc");

        result.IsRight.Should().BeTrue();
        capture
            .Seen.Should()
            .Equal(
                "Decide<ChoiceDecision<Lane>>#0",
                "Switch<Lane>#0",
                "Switch<Lane>#0/Left/Note#0",
                "Switch<Lane>#0/Left/Positive#0"
            );

        var drawn = Ids(graph.Nodes).ToList();
        capture.Seen.Should().OnlyContain(id => drawn.Contains(id));
    }

    [Test]
    public async Task CurrentNodeId_NumbersARepeatedJunctionAsTheGraphDoes()
    {
        var capture = new Capture();

        var result = await new RepeatingTrain(capture).RunEither("abc");

        result.IsRight.Should().BeTrue();
        capture.Seen.Should().Equal("Length#0", "Describe#0", "Length#1", "Positive#0");
    }

    [Test]
    public async Task CurrentNodeId_IsNullOutsideAStep()
    {
        await new LinearTrain().RunEither("abc");

        ChainGraph.CurrentNodeId.Should().BeNull();
    }

    private static ChainGraph GraphOf<TIn, TOut>(Train<TIn, TOut> train) =>
        ChainGraph.From(train.DeclaredChain(), train.GetType(), typeof(TIn), typeof(TOut));

    private static IEnumerable<string> Ids(IEnumerable<ChainGraphNode> nodes) =>
        nodes.SelectMany(n => Ids(n.Tracks.SelectMany(t => t.Nodes)).Prepend(n.Id));

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    /// <summary>Notes the node id each step reports, from junctions and the decision observer alike.</summary>
    private sealed class Capture : IDecisionObserver
    {
        public List<string> Seen { get; } = [];

        public void Note() => Seen.Add(ChainGraph.CurrentNodeId ?? "(none)");

        public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
        {
            Note();
            return Task.CompletedTask;
        }

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken)
        {
            Note();
            return Task.CompletedTask;
        }
    }

    private sealed class Length(Capture? capture = null) : Junction<string, int>
    {
        public override Task<int> Run(string input)
        {
            capture?.Note();
            return Task.FromResult(input.Length);
        }
    }

    private sealed class Describe(Capture? capture = null) : Junction<int, string>
    {
        public override Task<string> Run(int input)
        {
            capture?.Note();
            return Task.FromResult(new string('x', input));
        }
    }

    private sealed class Positive(Capture? capture = null) : Junction<int, bool>
    {
        public override Task<bool> Run(int input)
        {
            capture?.Note();
            return Task.FromResult(input > 0);
        }
    }

    private sealed class Note(Capture capture) : Junction<string, int>
    {
        public override Task<int> Run(string input)
        {
            capture.Note();
            return Task.FromResult(input.Length);
        }
    }

    private sealed class MaybePositive : Junction<long, bool>
    {
        public override Task<bool> Run(long input) => Task.FromResult(input > 0);
    }

    public interface ILength : IJunction<string, int>;

    private sealed class InterfaceLength : Junction<string, int>, ILength
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private sealed class LinearTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<Length>().Chain<Positive>().Resolve();
    }

    private sealed class LongerTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<Describe>().Chain<Length>().Chain<Positive>().Resolve();
    }

    private sealed class RepeatingTrain(Capture? capture = null) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain(new Length(capture))
                .Chain(new Describe(capture))
                .Chain(new Length(capture))
                .Chain(new Positive(capture))
                .Resolve();
    }

    private sealed class InterfaceTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<ILength>(new InterfaceLength())
                .IChain<ILength>()
                .Chain<Positive>()
                .Resolve();
    }

    private sealed class ExtractingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<Length>().Extract<int, long>().ShortCircuit<MaybePositive>().Resolve();
    }

    private sealed class DecidingTrain(Capture capture) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<IDecider, IDecisionObserver>(
                    new ScriptedDecider().Choose(Lane.Left),
                    capture
                )
                .Decide<string>(q => q.Choice<Lane>())
                .Switch<Lane>(s =>
                    s.When(Lane.Left, l => l.Chain(new Note(capture)).Chain(new Positive(capture)))
                        .When(
                            Lane.Right,
                            r => r.Chain(new Note(capture)).Chain(new Positive(capture))
                        )
                )
                .Resolve();
    }
}
