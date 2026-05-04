using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Providers.Scraper;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.MonitorJob;
using Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.MonitorJob;

[TestFixture]
public class RefetchAndHashJunctionTests
{
    private static Job NewJob(string? url) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            Title = "Engineer",
            Company = "Acme",
            Url = url,
            RawDescription = "raw description",
            Status = JobStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    [Test]
    public async Task Run_JobMissing_Throws()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var scraper = new Mock<IJobScraper>();
        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        Func<Task> act = () => junction.Run(new MonitorJobInput { JobId = Guid.NewGuid() });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public async Task Run_JobWithoutUrl_HashesRawDescription()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var job = NewJob(url: null);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>(MockBehavior.Strict);
        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        var ctx = await junction.Run(new MonitorJobInput { JobId = job.Id });

        ctx.JobId.Should().Be(job.Id);
        ctx.JobUrl.Should().BeEmpty();
        ctx.FetchedContent.Should().Be(job.RawDescription);
        ctx.ContentHash.Should().Be(Sha256Hex(job.RawDescription));
        ctx.Closed.Should().BeFalse();
        scraper.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Run_JobWithUrl_FetchesAndHashesScrapedContent()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var job = NewJob(url: "https://example.com/job");
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapeResult { Description = "fresh content" });

        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        var ctx = await junction.Run(new MonitorJobInput { JobId = job.Id });

        ctx.JobUrl.Should().Be(job.Url);
        ctx.FetchedContent.Should().Be("fresh content");
        ctx.ContentHash.Should().Be(Sha256Hex("fresh content"));
        ctx.Closed.Should().BeFalse();
    }

    [Test]
    public async Task Run_ScrapeReturnsNullDescription_FetchesEmpty()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var job = NewJob(url: "https://example.com/job");
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapeResult { Description = null });

        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        var ctx = await junction.Run(new MonitorJobInput { JobId = job.Id });

        ctx.FetchedContent.Should().BeEmpty();
        ctx.ContentHash.Should().Be(Sha256Hex(""));
    }

    [Test]
    public async Task Run_Scrape404_MarksClosed()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var job = NewJob(url: "https://example.com/missing");
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new HttpRequestException(
                    "Not found",
                    inner: null,
                    statusCode: System.Net.HttpStatusCode.NotFound
                )
            );

        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        var ctx = await junction.Run(new MonitorJobInput { JobId = job.Id });

        ctx.Closed.Should().BeTrue();
        ctx.FetchedContent.Should().BeEmpty();
        ctx.ContentHash.Should().BeEmpty();
    }

    [Test]
    public async Task Run_ScrapeNon404Error_PropagatesException()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var job = NewJob(url: "https://example.com/job");
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new HttpRequestException(
                    "Server error",
                    inner: null,
                    statusCode: System.Net.HttpStatusCode.InternalServerError
                )
            );

        var junction = new RefetchAndHashJunction(
            db,
            scraper.Object,
            NullLogger<RefetchAndHashJunction>.Instance
        );

        Func<Task> act = () => junction.Run(new MonitorJobInput { JobId = job.Id });

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
