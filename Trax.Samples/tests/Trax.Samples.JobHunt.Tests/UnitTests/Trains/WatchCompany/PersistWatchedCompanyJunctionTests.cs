using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.WatchCompany;
using Trax.Samples.JobHunt.Trains.WatchCompany.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.WatchCompany;

[TestFixture]
public class PersistWatchedCompanyJunctionTests
{
    [Test]
    public async Task Run_PersistsWatchedCompany()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var junction = new PersistWatchedCompanyJunction(
            db,
            NullLogger<PersistWatchedCompanyJunction>.Instance
        );

        var result = await junction.Run(
            new WatchCompanyInput
            {
                UserId = "alice",
                CompanyName = "Acme",
                CareersUrl = "https://acme.com/careers",
            }
        );

        result.WatchedCompanyId.Should().NotBeEmpty();
        result.CompanyName.Should().Be("Acme");

        var entry = await db.WatchedCompanies.SingleAsync();
        entry.UserId.Should().Be("alice");
        entry.CareersUrl.Should().Be("https://acme.com/careers");
    }
}
