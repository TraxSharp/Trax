using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Providers.Scraper;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies;
using Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.MonitorAllWatchedCompanies;

[TestFixture]
public class FanOutCompanyMonitorJunctionTests
{
    private static WatchedCompany NewCompany(string name, string url) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            CompanyName = name,
            CareersUrl = url,
            CreatedAt = DateTime.UtcNow,
        };

    [Test]
    public async Task Run_NoCompanies_ReturnsZero()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var scraper = new Mock<IJobScraper>(MockBehavior.Strict);
        var junction = new FanOutCompanyMonitorJunction(
            db,
            scraper.Object,
            NullLogger<FanOutCompanyMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllWatchedCompaniesInput());

        output.CompaniesChecked.Should().Be(0);
        scraper.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Run_AllCompaniesScrapeSuccessfully_UpdatesFingerprintAndCheckedAt()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var c1 = NewCompany("Acme", "https://acme.example/careers");
        var c2 = NewCompany("Globex", "https://globex.example/careers");
        db.WatchedCompanies.AddRange(c1, c2);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapeResult { Description = "openings" });

        var junction = new FanOutCompanyMonitorJunction(
            db,
            scraper.Object,
            NullLogger<FanOutCompanyMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllWatchedCompaniesInput());

        output.CompaniesChecked.Should().Be(2);
        var reloaded = await db.WatchedCompanies.AsNoTracking().ToListAsync();
        reloaded
            .Should()
            .AllSatisfy(c =>
            {
                c.LastFingerprint.Should().NotBeNullOrEmpty();
                c.LastCheckedAt.Should().NotBeNull();
            });
    }

    [Test]
    public async Task Run_ScraperFails_LogsAndContinuesOtherCompanies()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var c1 = NewCompany("Bad", "https://bad.example/careers");
        var c2 = NewCompany("Good", "https://good.example/careers");
        db.WatchedCompanies.AddRange(c1, c2);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s =>
                s.ScrapeAsync(
                    It.Is<Uri>(u => u.Host == "bad.example"),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new HttpRequestException("nope"));
        scraper
            .Setup(s =>
                s.ScrapeAsync(
                    It.Is<Uri>(u => u.Host == "good.example"),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new ScrapeResult { Description = "ok" });

        var junction = new FanOutCompanyMonitorJunction(
            db,
            scraper.Object,
            NullLogger<FanOutCompanyMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllWatchedCompaniesInput());

        output.CompaniesChecked.Should().Be(2);
        var reloadedGood = await db
            .WatchedCompanies.AsNoTracking()
            .FirstAsync(c => c.CompanyName == "Good");
        reloadedGood.LastFingerprint.Should().NotBeNullOrEmpty();
        var reloadedBad = await db
            .WatchedCompanies.AsNoTracking()
            .FirstAsync(c => c.CompanyName == "Bad");
        reloadedBad.LastFingerprint.Should().BeNull();
    }

    [Test]
    public async Task Run_NullScrapeDescription_FingerprintsEmpty()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var c1 = NewCompany("Acme", "https://acme.example/careers");
        db.WatchedCompanies.Add(c1);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapeResult { Description = null });

        var junction = new FanOutCompanyMonitorJunction(
            db,
            scraper.Object,
            NullLogger<FanOutCompanyMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllWatchedCompaniesInput());

        output.CompaniesChecked.Should().Be(1);
        var reloaded = await db.WatchedCompanies.AsNoTracking().FirstAsync();
        reloaded.LastFingerprint.Should().NotBeNullOrEmpty();
    }
}
