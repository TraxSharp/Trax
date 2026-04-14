using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.MonitorJob;
using Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.MonitorJob;

[TestFixture]
public class DiffAgainstLastSnapshotJunctionTests
{
    [Test]
    public async Task Run_FirstSnapshot_NoDiff()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new DiffAgainstLastSnapshotJunction(db);

        var ctx = new MonitorJobContext
        {
            JobId = Guid.NewGuid(),
            JobUrl = "https://example.com",
            FetchedContent = "content",
            ContentHash = "abc123",
        };

        var result = await junction.Run(ctx);

        result.Changed.Should().BeFalse();
        result.PreviousHash.Should().BeNull();
    }

    [Test]
    public async Task Run_SameContent_NoDiff()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.JobSnapshots.Add(
            new JobSnapshot
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FetchedAt = DateTime.UtcNow.AddHours(-1),
                ContentHash = "abc123",
                RawContent = "content",
            }
        );
        await db.SaveChangesAsync();

        var junction = new DiffAgainstLastSnapshotJunction(db);
        var result = await junction.Run(
            new MonitorJobContext
            {
                JobId = jobId,
                JobUrl = "https://example.com",
                FetchedContent = "content",
                ContentHash = "abc123",
            }
        );

        result.Changed.Should().BeFalse();
        result.DiffSummary.Should().BeNull();
    }

    [Test]
    public async Task Run_DifferentContent_ReturnsSummary()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobId = Guid.NewGuid();
        db.JobSnapshots.Add(
            new JobSnapshot
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FetchedAt = DateTime.UtcNow.AddHours(-1),
                ContentHash = "oldhash",
                RawContent = "old content",
            }
        );
        await db.SaveChangesAsync();

        var junction = new DiffAgainstLastSnapshotJunction(db);
        var result = await junction.Run(
            new MonitorJobContext
            {
                JobId = jobId,
                JobUrl = "https://example.com",
                FetchedContent = "new content",
                ContentHash = "newhash",
            }
        );

        result.Changed.Should().BeTrue();
        result.DiffSummary.Should().Contain("changed");
    }

    [Test]
    public async Task Run_Closed_ReportsChanged()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new DiffAgainstLastSnapshotJunction(db);

        var result = await junction.Run(
            new MonitorJobContext
            {
                JobId = Guid.NewGuid(),
                JobUrl = "https://example.com",
                FetchedContent = "",
                ContentHash = "",
                Closed = true,
            }
        );

        result.Changed.Should().BeTrue();
        result.DiffSummary.Should().Contain("no longer available");
    }
}
