using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.CreateApplication;
using Trax.Samples.JobHunt.Trains.CreateApplication.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.CreateApplication;

[TestFixture]
public class PersistApplicationJunctionTests
{
    [Test]
    public async Task Run_PersistsApplication()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistApplicationJunction(
            db,
            NullLogger<PersistApplicationJunction>.Instance
        );

        var result = await junction.Run(
            new CreateApplicationInput { JobId = Guid.NewGuid(), UserId = "alice" }
        );

        result.ApplicationId.Should().NotBeEmpty();
        result.Status.Should().Be("Drafted");

        var app = await db.Applications.SingleAsync();
        app.UserId.Should().Be("alice");
        app.Status.Should().Be(ApplicationStatus.Drafted);
    }
}
