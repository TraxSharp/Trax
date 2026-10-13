using Trax.Effect.StateMachine.Testing;
using Trax.Samples.Recovery.E2E.Utilities;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The topic map's committed differential corpus, which the page's twin enumerates (its
/// <c>src/topicMap/differential.test.ts</c> holds the twin to the same file), replayed through the C# engine:
/// the two runtimes agree on every case, so the page never offers a move the server refuses.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TopicMapDifferentialTests
{
    [Test]
    public void TopicMapMachine_reproduces_its_committed_differential_corpus()
    {
        File.Exists(TopicMapArtifacts.DifferentialCorpus)
            .Should()
            .BeTrue(
                $"the corpus is committed at {TopicMapArtifacts.DifferentialCorpus}; regenerate it with the "
                    + "command in the sample's README"
            );

        var diffs = DifferentialCorpus.Replay(
            TopicMapArtifacts.Engine(),
            File.ReadAllText(TopicMapArtifacts.DifferentialCorpus)
        );

        diffs
            .Should()
            .BeEmpty(
                "the server applies every transition the twin predicts, and both engines must agree "
                    + "(Trax.Docs/adr/0048):\n"
                    + string.Join("\n", diffs)
            );
    }
}
