using AwesomeAssertions;
using CsCheck;
using Trax.Core.Monad;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// The laws a <see cref="ChainGraph"/>'s node ids and JSON keep for any chain, checked over
/// generated cases rather than the handful of trains in <see cref="ChainGraphTests"/>. Tools key
/// recorded runs, decisions and checkpoints on these ids, so they must hold for chains nobody wrote
/// a test for.
///
/// <para>A failure prints the seed CsCheck shrank it to. Pin it as its own test with
/// <c>Sample(..., seed: "...")</c> before fixing the code, so the case stays covered.</para>
/// </summary>
public class ChainGraphPropertyTests
{
    // A small alphabet, so a generated sequence repeats keys often and the ordinals are exercised.
    private static readonly Gen<string> Key = Gen.Int[0, 5].Select(i => $"Junction{i}");

    private static readonly Gen<List<string>> Keys = Key.List[0, 40];

    /// <summary>Numbers <paramref name="keys"/> in one fresh scope, the way a chain's steps are.</summary>
    private static List<string> Number(IEnumerable<string> keys, string prefix = "")
    {
        var scope = new ChainNodeScope(prefix);
        return keys.Select(scope.Next).ToList();
    }

    [Test]
    public void EveryIdInAScope_IsUnique() =>
        Keys.Sample(keys => Number(keys).Should().OnlyHaveUniqueItems());

    [Test]
    public void TwoNumberingsOfTheSameKeys_GiveTheSameIds() =>
        Keys.Sample(keys => Number(keys).Should().Equal(Number(keys)));

    [Test]
    public void IdsInsideATrack_AreUniqueAcrossTheTrackAndItsParent() =>
        Gen.Select(Keys, Keys)
            .Sample(
                (outer, inner) =>
                {
                    var scope = new ChainNodeScope("");
                    var routing = scope.Next("Switch<Source>");
                    var ids = outer.Select(scope.Next).ToList();
                    var track = scope.Track(routing, "Papers");
                    ids.AddRange(inner.Select(track.Next));

                    ids.Should().OnlyHaveUniqueItems().And.NotContain(routing);
                }
            );

    [Test]
    public void InsertingAKeyNoOtherStepHas_MovesNoOtherId() =>
        Gen.Select(Keys, Gen.Int[0, 40])
            .Sample(
                (keys, at) =>
                {
                    var position = Math.Min(at, keys.Count);
                    var longer = keys.ToList();
                    longer.Insert(position, "Inserted");

                    var before = Number(keys);
                    var after = Number(longer);
                    after.RemoveAt(position);

                    after.Should().Equal(before, "an id names its step, not its position");
                }
            );

    [Test]
    public void ToJson_OfTheSameGraph_IsTheSameText() =>
        Graph.Sample(graph =>
        {
            var json = graph.ToJson();

            graph.ToJson().Should().Be(json);
            (graph with { }).ToJson().Should().Be(json);
            graph.Hash.Should().Be(graph.Hash).And.MatchRegex("^[0-9a-f]{64}$");
        });

    // Text with the characters JSON escapes and the ones the relaxed encoder lets through.
    private static readonly Gen<string> Text = Gen.Char["abcXYZ019 <>+&'\"\\/#é中"]
        .Array[0, 12]
        .Select(chars => new string(chars));

    private static readonly Gen<string?> OptionalText = Gen.Select(
        Gen.Bool,
        Text,
        (present, text) => present ? text : null
    );

    private static readonly Gen<ChainStepKind> Kind = Gen.Enum<ChainStepKind>();

    private static Gen<ChainGraphNode> Node(int depth) =>
        Gen.Select(
            Text,
            Kind,
            OptionalText,
            OptionalText,
            OptionalText,
            Gen.Bool,
            depth == 0
                ? Gen.Bool.Select(_ => new List<ChainGraphTrack>())
                : Track(depth - 1).List[0, 3],
            (id, kind, junction, input, output, opaque, tracks) =>
                new ChainGraphNode(id, kind, junction, input, output, opaque, tracks)
        );

    private static Gen<ChainGraphTrack> Track(int depth) =>
        Gen.Select(
            Text,
            OptionalText,
            Gen.Bool,
            Node(depth).List[0, 4],
            (name, description, fallback, nodes) =>
                new ChainGraphTrack(name, description, fallback, nodes)
        );

    private static readonly Gen<ChainGraph> Graph = Gen.Select(
        Text,
        Text,
        Text,
        Node(2).List[0, 6],
        Text.List[0, 3],
        (train, input, output, nodes, refusals) =>
            new ChainGraph(train, input, output, nodes, refusals)
    );
}
