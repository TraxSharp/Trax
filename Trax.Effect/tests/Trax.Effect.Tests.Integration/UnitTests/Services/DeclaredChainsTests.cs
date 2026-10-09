using AwesomeAssertions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fakes.Trains;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// A train class's declared chain is kept for the process once it is read, but a read that fails
/// is not: the next run reads it again, so a train whose construction failed once (a dependency
/// down while it was built) checkpoints and resumes again once it can be built.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md"
)]
public class DeclaredChainsTests
{
    [Test]
    public void A_chain_that_failed_to_read_once_is_read_again_and_then_kept()
    {
        var reads = 0;
        var failures = new List<Exception>();

        ChainRecorder Declare()
        {
            reads++;
            if (reads == 1)
                throw new InvalidOperationException("a dependency was down");

            return new OnceUnbuildableTrain().DeclaredChain();
        }

        DeclaredChains
            .For(
                typeof(OnceUnbuildableTrain),
                typeof(string),
                typeof(string),
                Declare,
                failures.Add
            )
            .Should()
            .BeNull("the first read failed");

        var second = DeclaredChains.For(
            typeof(OnceUnbuildableTrain),
            typeof(string),
            typeof(string),
            Declare,
            failures.Add
        );

        second.Should().NotBeNull("a failed read is not remembered, so the next run reads again");
        second!.Checkpoints.Should().BeTrue();

        DeclaredChains
            .For(
                typeof(OnceUnbuildableTrain),
                typeof(string),
                typeof(string),
                Declare,
                failures.Add
            )
            .Should()
            .BeSameAs(second, "a read that succeeded is kept");

        reads.Should().Be(2);
        failures.Should().ContainSingle();
    }

    /// <summary>A train only this test reads, so no other test has its chain remembered.</summary>
    private sealed class OnceUnbuildableTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<PlanResearch>()
                .Chain<SearchWebFromPlan>()
                .Chain<FetchFullTexts>()
                .Checkpoint<CheckedFindings>()
                .Chain<ScoreFindings>()
                .Chain<Summarize>()
                .Resolve();
    }
}
