using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Tests.Integration.Fakes.Trains;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// An operator's run graph asks whether a run can resume at every node at once. It reads the run's
/// lineage once, without any checkpoint's state, and decides every verdict from the rows' nodes,
/// hashes and fingerprints: a state can be a megabyte, and the graph shows none of it.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md"
)]
public class RunResumesTests
{
    private const string Checkpoint = "Checkpoint<CheckedFindings>#0";

    [Test]
    public async Task Checking_every_node_reads_the_lineage_once_and_no_state()
    {
        var chain = new ResearchTrain().DeclaredChain();
        var rows = new CountingRows(
            new Trax.Effect.Models.Checkpoint.Checkpoint
            {
                Id = 7,
                MetadataId = 1,
                NodeId = Checkpoint,
                StateType = typeof(CheckedFindings).FullName!,
                State = null!,
                Tracks =
                    """[{"key":"Trax.Effect.Tests.Integration.Fakes.Trains.ResearchSource","track":"Papers","fallbackReason":null}]""",
                ChainHash = ChainGraph
                    .From(chain, typeof(ResearchTrain), typeof(string), typeof(string))
                    .Hash,
                StateFingerprint = Fingerprint(typeof(CheckedFindings)),
                CreatedAt = DateTime.UtcNow,
            }
        );
        var resumes = new RunResumes([rows], new ServiceCollection().BuildServiceProvider());
        string[] points = ["FetchFullTexts#0", Checkpoint, "ScoreFindings#0", "Summarize#0"];

        var checks = await resumes.CheckMany(
            typeof(ResearchTrain),
            chain,
            typeof(string),
            typeof(string),
            1,
            points,
            CancellationToken.None
        );

        checks.Latest.CanResume.Should().BeTrue(checks.Latest.Reason);
        checks.At["ScoreFindings#0"].CanResume.Should().BeTrue();
        checks.Checkpoints.Should().Equal([Checkpoint]);
        rows.Lineages.Should().Be(1, "one lineage read serves every point");
        rows.StateReads.Should().Be(0, "a verdict reads no checkpoint's state");
    }

    private static string Fingerprint(Type state) =>
        (string)
            typeof(ChainGraph)
                .Assembly.GetType("Trax.Core.Monad.CheckpointState")!
                .GetMethod("Fingerprint")!
                .Invoke(null, [state])!;

    /// <summary>One run with the given rows, counting what is read.</summary>
    private sealed class CountingRows(params Trax.Effect.Models.Checkpoint.Checkpoint[] rows)
        : ICheckpointRows
    {
        public int Lineages { get; private set; }

        public int StateReads { get; private set; }

        public Task Insert(
            Trax.Effect.Models.Checkpoint.Checkpoint row,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<ResumedRun>> Lineage(
            long runId,
            CancellationToken cancellationToken
        )
        {
            Lineages++;
            return Task.FromResult<IReadOnlyList<ResumedRun>>([
                new ResumedRun(runId, null, null, rows),
            ]);
        }

        public Task<IReadOnlyDictionary<long, string>> States(
            IReadOnlyCollection<long> rowIds,
            CancellationToken cancellationToken
        )
        {
            StateReads++;
            return Task.FromResult<IReadOnlyDictionary<long, string>>(
                new Dictionary<long, string>()
            );
        }

        public Task<ResumeSource?> Source(long runId, CancellationToken cancellationToken) =>
            Task.FromResult<ResumeSource?>(null);

        public Task DeleteFor(long runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool HasUncommittedWork(IServiceProvider scope) => false;
    }
}
