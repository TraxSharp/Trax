using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.ListJobs;
using Trax.Samples.JobHunt.Trains.ListJobs.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.ListJobs;

[TestFixture]
public class LoadJobsJunctionTests
{
    [Test]
    public async Task Run_NoJobs_ReturnsEmptyList()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new LoadJobsJunction(db);
        var input = new ListJobsInput { UserId = "alice" };

        var result = await junction.Run(input);

        result.Jobs.Should().BeEmpty();
    }

    [Test]
    public async Task Run_WithJobs_ReturnsMatchingUser()
    {
        await using var db = JobHuntDbContextFixture.Create();
        db.Jobs.AddRange(
            MakeJob("alice", "Engineer", "Acme"),
            MakeJob("bob", "Designer", "OtherCo")
        );
        await db.SaveChangesAsync();

        var junction = new LoadJobsJunction(db);
        var result = await junction.Run(new ListJobsInput { UserId = "alice" });

        result.Jobs.Should().ContainSingle();
        result.Jobs[0].Title.Should().Be("Engineer");
    }

    [Test]
    public async Task Run_StatusFilter_ReturnsOnlyMatching()
    {
        await using var db = JobHuntDbContextFixture.Create();
        db.Jobs.AddRange(
            MakeJob("alice", "Active Job", "Acme", JobStatus.Active),
            MakeJob("alice", "Closed Job", "Acme", JobStatus.Closed)
        );
        await db.SaveChangesAsync();

        var junction = new LoadJobsJunction(db);
        var result = await junction.Run(
            new ListJobsInput { UserId = "alice", Status = JobStatus.Active }
        );

        result.Jobs.Should().ContainSingle();
        result.Jobs[0].Title.Should().Be("Active Job");
    }

    [Test]
    public async Task Run_OrdersNewestFirst()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var older = MakeJob("alice", "Old Job", "Acme");
        older.CreatedAt = DateTime.UtcNow.AddDays(-1);
        var newer = MakeJob("alice", "New Job", "Acme");
        newer.CreatedAt = DateTime.UtcNow;
        db.Jobs.AddRange(older, newer);
        await db.SaveChangesAsync();

        var junction = new LoadJobsJunction(db);
        var result = await junction.Run(new ListJobsInput { UserId = "alice" });

        result.Jobs.Should().HaveCount(2);
        result.Jobs[0].Title.Should().Be("New Job");
        result.Jobs[1].Title.Should().Be("Old Job");
    }

    [Test]
    public async Task Run_DoesNotLeakAcrossUsers()
    {
        await using var db = JobHuntDbContextFixture.Create();
        db.Jobs.AddRange(
            MakeJob("alice", "Alice Job", "Acme"),
            MakeJob("bob", "Bob Job", "OtherCo")
        );
        await db.SaveChangesAsync();

        var junction = new LoadJobsJunction(db);
        var result = await junction.Run(new ListJobsInput { UserId = "bob" });

        result.Jobs.Should().ContainSingle();
        result.Jobs[0].Title.Should().Be("Bob Job");
    }

    private static Job MakeJob(
        string userId,
        string title,
        string company,
        JobStatus status = JobStatus.Active
    )
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Company = company,
            RawDescription = "Description",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }
}
