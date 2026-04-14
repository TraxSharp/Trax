using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.MonitorJob;
using Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.MonitorJob;

[TestFixture]
public class PersistSnapshotAndUpdateJobJunctionTests
{
    [Test]
    public async Task Run_PersistsSnapshot()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistSnapshotAndUpdateJobJunction(
            db,
            NullLogger<PersistSnapshotAndUpdateJobJunction>.Instance
        );

        var jobId = Guid.NewGuid();
        var result = await junction.Run(
            new MonitorJobContext
            {
                JobId = jobId,
                JobUrl = "https://example.com",
                FetchedContent = "content",
                ContentHash = "hash123",
                Changed = false,
            }
        );

        var snapshot = await db.JobSnapshots.SingleAsync();
        snapshot.JobId.Should().Be(jobId);
        snapshot.ContentHash.Should().Be("hash123");
        snapshot.RawContent.Should().Be("content");
        result.Changed.Should().BeFalse();
    }

    [Test]
    public async Task Run_Closed_UpdatesJobStatus()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.Jobs.Add(
            new Job
            {
                Id = jobId,
                UserId = "alice",
                Title = "Dev",
                Company = "Co",
                RawDescription = "Work",
                Status = JobStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new PersistSnapshotAndUpdateJobJunction(
            db,
            NullLogger<PersistSnapshotAndUpdateJobJunction>.Instance
        );

        await junction.Run(
            new MonitorJobContext
            {
                JobId = jobId,
                JobUrl = "https://example.com",
                FetchedContent = "",
                ContentHash = "",
                Closed = true,
                Changed = true,
                DiffSummary = "Job closed",
            }
        );

        var job = await db.Jobs.SingleAsync();
        job.Status.Should().Be(JobStatus.Closed);
    }

    [Test]
    public async Task Run_NotClosed_LeavesJobActive()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.Jobs.Add(
            new Job
            {
                Id = jobId,
                UserId = "alice",
                Title = "Dev",
                Company = "Co",
                RawDescription = "Work",
                Status = JobStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var junction = new PersistSnapshotAndUpdateJobJunction(
            db,
            NullLogger<PersistSnapshotAndUpdateJobJunction>.Instance
        );

        await junction.Run(
            new MonitorJobContext
            {
                JobId = jobId,
                JobUrl = "https://example.com",
                FetchedContent = "content",
                ContentHash = "hash",
            }
        );

        var job = await db.Jobs.SingleAsync();
        job.Status.Should().Be(JobStatus.Active);
    }
}
