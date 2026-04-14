using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.AddJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.AddJob;

[TestFixture]
public class PersistJobJunctionTests
{
    [Test]
    public async Task Run_ValidInput_PersistsRow()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistJobJunction(db, NullLogger<PersistJobJunction>.Instance);
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Senior Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Build distributed systems",
        };

        var result = await junction.Run(input);

        var persisted = await db.Jobs.SingleAsync();
        persisted.Id.Should().Be(result.JobId);
        persisted.Title.Should().Be("Senior Engineer");
        persisted.Company.Should().Be("Acme");
        persisted.RawDescription.Should().Be("Build distributed systems");
        persisted.UserId.Should().Be("alice");
    }

    [Test]
    public async Task Run_PersistedRow_HasActiveStatus()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistJobJunction(db, NullLogger<PersistJobJunction>.Instance);
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Work",
        };

        await junction.Run(input);

        var job = await db.Jobs.SingleAsync();
        job.Status.Should().Be(JobStatus.Active);
    }

    [Test]
    public async Task Run_AssignsCreatedAtAndUpdatedAt()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistJobJunction(db, NullLogger<PersistJobJunction>.Instance);
        var before = DateTime.UtcNow;
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Engineer",
            PastedCompany = "Acme",
            PastedDescription = "Work",
        };

        await junction.Run(input);

        var job = await db.Jobs.SingleAsync();
        job.CreatedAt.Should().BeOnOrAfter(before);
        job.UpdatedAt.Should().BeOnOrAfter(before);
        job.CreatedAt.Should().Be(job.UpdatedAt);
    }

    [Test]
    public async Task Run_ReturnsCorrectOutput()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistJobJunction(db, NullLogger<PersistJobJunction>.Instance);
        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Staff Engineer",
            PastedCompany = "MegaCorp",
            PastedDescription = "Lead things",
        };

        var result = await junction.Run(input);

        result.JobId.Should().NotBeEmpty();
        result.Title.Should().Be("Staff Engineer");
        result.Company.Should().Be("MegaCorp");
    }
}
