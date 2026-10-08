using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.Checkpoint;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The <c>trax.checkpoint</c> table and the resume links as migration 074 builds them: a row
/// round-trips through <see cref="IDataContext.Checkpoints"/>, a run holds one row per node, its rows
/// go with it, and one queued entry at a time resumes a given run
/// (<c>ix_work_queue_unique_queued_resume</c>).
/// </summary>
[TestFixture]
public class CheckpointTableTests : TestSetup
{
    [Test]
    public async Task A_checkpoint_round_trips_with_its_defaults()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var run = await NewRun(context);
        context.Checkpoints.Add(
            Row(run, "Parallel#0/embedding/Checkpoint<Scores>#0", "Parallel#0/embedding")
        );
        await context.SaveChanges(CancellationToken.None);

        var stored = await context.Checkpoints.AsNoTracking().SingleAsync(c => c.MetadataId == run);

        stored.NodeId.Should().Be("Parallel#0/embedding/Checkpoint<Scores>#0");
        stored.BranchPath.Should().Be("Parallel#0/embedding");
        stored.StateType.Should().Be("Scores");
        stored.State.Should().Be("""{"score": 3}""");
        stored.Tracks.Should().Be("[]");
        stored.ChainHash.Should().Be(new string('a', 64));
        stored.StateFingerprint.Should().Be("fp");

        await DeleteRun(context, run);
    }

    [Test]
    public async Task A_second_row_for_one_run_and_node_is_refused()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var run = await NewRun(context);
        context.Checkpoints.Add(Row(run, "Checkpoint<Scores>#0", null));
        await context.SaveChanges(CancellationToken.None);

        using var other = (IDataContext)DataContextFactory.Create();
        other.Checkpoints.Add(Row(run, "Checkpoint<Scores>#0", null));
        var second = () => other.SaveChanges(CancellationToken.None);

        await second.Should().ThrowAsync<DbUpdateException>();

        await DeleteRun(context, run);
    }

    [Test]
    public async Task A_runs_checkpoints_are_deleted_with_it()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var run = await NewRun(context);
        var kept = await NewRun(context);
        context.Checkpoints.Add(Row(run, "Checkpoint<Scores>#0", null));
        context.Checkpoints.Add(Row(kept, "Checkpoint<Scores>#0", null));
        await context.SaveChanges(CancellationToken.None);

        await DeleteRun(context, run);

        (await context.Checkpoints.AsNoTracking().CountAsync(c => c.MetadataId == run))
            .Should()
            .Be(0);
        (await context.Checkpoints.AsNoTracking().CountAsync(c => c.MetadataId == kept))
            .Should()
            .Be(1, "only the deleted run's rows go with it");

        await DeleteRun(context, kept);
    }

    [Test]
    public async Task A_second_queued_resume_of_one_run_is_refused()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var source = await NewRun(context);
        await context.Track(Resuming(source));
        await context.SaveChanges(CancellationToken.None);

        await context.Track(Resuming(source));
        var second = () => context.SaveChanges(CancellationToken.None);

        await second.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    [LeavesStuckRuns(
        "marks an entry dispatched by hand to exercise the partial index, with no run behind it"
    )]
    public async Task A_dispatched_resume_does_not_count_against_a_queued_one()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var source = await NewRun(context);
        var dispatched = Resuming(source);
        dispatched.Status = WorkQueueStatus.Dispatched;
        await context.Track(dispatched);
        await context.Track(Resuming(source));

        var act = () => context.SaveChanges(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static WorkQueue Resuming(long source)
    {
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "Resume.Unique.Train" });
        entry.ResumeFrom = source;
        entry.ResumeAt = "Checkpoint<Scores>#0";
        return entry;
    }

    private static Checkpoint Row(long run, string nodeId, string? branchPath) =>
        new()
        {
            MetadataId = run,
            NodeId = nodeId,
            BranchPath = branchPath,
            StateType = "Scores",
            State = """{"score": 3}""",
            ChainHash = new string('a', 64),
            StateFingerprint = "fp",
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task<long> NewRun(IDataContext context)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Checkpoint.Table.Train",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        context.Metadatas.Add(metadata);
        await context.SaveChanges(CancellationToken.None);
        return metadata.Id;
    }

    private static Task DeleteRun(IDataContext context, long run) =>
        context.Metadatas.Where(m => m.Id == run).ExecuteDeleteAsync();
}
