using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Services.ManifestPruning;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Metadata cleanup and the manifest prune against the resume links, on Postgres and SQLite: a run
/// is kept while a queued entry or a run that stays resumes it, since its checkpoints go with it;
/// a resumed run and its source go together once both have expired; and no row is left naming a
/// run that is gone (<c>TraxInvariants</c>' <c>resume-from-missing-run</c>).
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
/// <remarks>
/// Not on InMemory: metadata cleanup and the prune need a relational store (set deletes and
/// transactions), and a host on InMemory does not register them.
/// </remarks>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property("adr", Adr)]
public class CheckpointCleanupTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private static readonly TimeSpan Expired = TimeSpan.FromHours(2);

    private readonly ResearchDecider _decider = new();
    private ResearchCluster _cluster = null!;

    [OneTimeSetUp]
    public async Task CreateCluster() =>
        _cluster = await ResearchCluster.Create(
            store,
            _decider,
            s =>
                s.AddMetadataCleanup(c =>
                {
                    c.RetentionPeriod = TimeSpan.FromHours(1);
                    c.AddTrainType<ICheckpointResearchTrain>();
                })
        );

    [OneTimeTearDown]
    public async Task DisposeCluster() => await _cluster.DisposeAsync();

    [SetUp]
    public async Task Reset()
    {
        ResearchProbe.Reset();
        _decider.Reset();
        await _cluster.Reset();
    }

    [TearDown]
    public void ResetProbe() => ResearchProbe.Reset();

    [Test]
    public async Task Cleanup_keeps_a_source_run_while_a_queued_resume_names_it()
    {
        var manifest = await _cluster.Manifest("kept", maxRetries: 0);
        var source = await Crash(manifest);
        var resume = await _cluster.Operation(o =>
            o.ResumeExecutionAsync(source.Id, null, CancellationToken.None)
        );
        resume.Success.Should().BeTrue(resume.Message);
        await _cluster.Host.Age(source.Id, Expired);

        await Cleanup();

        (await Exists(source.Id)).Should().BeTrue($"a queued resume needs its checkpoints ({Adr})");
        (await _cluster.Checkpoints(source.Id)).Should().NotBeEmpty();

        // The operator cancels the resume: nothing will read the link again, the run goes, and
        // the cancelled entry no longer names it.
        (
            await _cluster.Operation(o =>
                o.CancelWorkQueueEntryAsync(resume.Id!.Value, CancellationToken.None)
            )
        )
            .Success.Should()
            .BeTrue();

        await Cleanup();

        (await Exists(source.Id)).Should().BeFalse();
        (await _cluster.Entry(resume.Id!.Value)).ResumeFrom.Should().BeNull();
        (await _cluster.ResumesOfMissingRuns()).Should().BeEmpty();
    }

    [Test]
    public async Task Cleanup_keeps_a_source_run_while_a_run_that_stays_resumes_it_and_deletes_both_once_expired()
    {
        var manifest = await _cluster.Manifest("lineage");
        var source = await Crash(manifest);
        var resumed = await _cluster.Cycle(manifest);
        resumed.ResumeFrom.Should().Be(source.Id);
        await _cluster.Host.Age(source.Id, Expired);

        await Cleanup();

        (await Exists(source.Id))
            .Should()
            .BeTrue("the run that resumed it has not expired, and may itself be resumed");

        await _cluster.Host.Age(resumed.Id, Expired);
        await Cleanup();

        (await Exists(source.Id)).Should().BeFalse("both expired, so both go together");
        (await Exists(resumed.Id)).Should().BeFalse();
        (await _cluster.ResumesOfMissingRuns()).Should().BeEmpty();
    }

    [Test]
    public async Task Cleanup_deletes_a_runs_checkpoints_with_it()
    {
        var manifest = await _cluster.Manifest("gone", maxRetries: 0);
        var source = await Crash(manifest);
        (await _cluster.Checkpoints(source.Id)).Should().NotBeEmpty();
        await _cluster.Host.Age(source.Id, Expired);

        await Cleanup();

        (await Exists(source.Id)).Should().BeFalse();
        (await _cluster.Checkpoints(source.Id))
            .Should()
            .BeEmpty($"a run's checkpoints are deleted with it ({Adr})");
    }

    [Test]
    public async Task A_manifest_prune_leaves_no_resume_naming_a_deleted_run()
    {
        var manifest = await _cluster.Manifest("pruned", maxRetries: 0);
        var source = await Crash(manifest);

        // An operator's resume belongs to no manifest, so the prune keeps it and clears its link.
        var resume = await _cluster.Operation(o =>
            o.ResumeExecutionAsync(source.Id, null, CancellationToken.None)
        );
        resume.Success.Should().BeTrue(resume.Message);

        using (var scope = _cluster.Host.Services.CreateScope())
        {
            var (pruned, _) = await ManifestPruner.PruneAsync(
                scope.ServiceProvider.GetRequiredService<IDataContext>(),
                [manifest.Id],
                NullLogger.Instance,
                CancellationToken.None
            );
            pruned.Should().Be(1);
        }

        (await Exists(source.Id)).Should().BeFalse();
        var entry = await _cluster.Entry(resume.Id!.Value);
        entry.ResumeFrom.Should().BeNull("it now reruns from the top");
        entry.ResumeAt.Should().BeNull();
        (await _cluster.ResumesOfMissingRuns()).Should().BeEmpty();
    }

    private async Task<Metadata> Crash(Effect.Models.Manifest.Manifest manifest)
    {
        ResearchProbe.FailIn = nameof(SummarizeLong);
        var failed = await _cluster.Cycle(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        ResearchProbe.Reset();
        return failed;
    }

    private async Task Cleanup()
    {
        using var scope = _cluster.Host.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMetadataCleanupTrain>()
            .Run(new MetadataCleanupRequest());
    }

    private Task<bool> Exists(long runId) =>
        _cluster.With(d => d.Metadatas.AsNoTracking().AnyAsync(m => m.Id == runId));
}
