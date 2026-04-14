using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Trax.Samples.JobHunt.Providers.Scraper;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.AddJob.Junctions;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.AddJob;

[TestFixture]
public class FetchJobPostingJunctionTests
{
    [Test]
    public async Task Run_UrlSet_CallsScraperAndMergesResult()
    {
        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ScrapeResult
                {
                    Title = "Scraped Title",
                    Company = "Scraped Co",
                    Description = "Scraped desc",
                }
            );

        var junction = new FetchJobPostingJunction(
            scraper.Object,
            NullLogger<FetchJobPostingJunction>.Instance
        );

        var input = new AddJobInput { UserId = "alice", Url = "https://example.com/job" };

        var result = await junction.Run(input);

        result.PastedTitle.Should().Be("Scraped Title");
        result.PastedCompany.Should().Be("Scraped Co");
        result.PastedDescription.Should().Be("Scraped desc");
        scraper.Verify(
            s => s.ScrapeAsync(new Uri("https://example.com/job"), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Test]
    public async Task Run_PastedFieldsSet_DoesNotCallScraper()
    {
        var scraper = new Mock<IJobScraper>();
        var junction = new FetchJobPostingJunction(
            scraper.Object,
            NullLogger<FetchJobPostingJunction>.Instance
        );

        var input = new AddJobInput
        {
            UserId = "alice",
            PastedTitle = "Already Set",
            PastedCompany = "AlreadyCo",
            PastedDescription = "Already done",
        };

        var result = await junction.Run(input);

        result.Should().BeSameAs(input);
        scraper.Verify(
            s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Test]
    public void Run_ScraperThrows_Propagates()
    {
        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var junction = new FetchJobPostingJunction(
            scraper.Object,
            NullLogger<FetchJobPostingJunction>.Instance
        );

        var input = new AddJobInput { UserId = "alice", Url = "https://example.com/job" };

        var act = () => junction.Run(input);

        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task Run_ScraperReturnsNulls_FillsDefaults()
    {
        var scraper = new Mock<IJobScraper>();
        scraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapeResult());

        var junction = new FetchJobPostingJunction(
            scraper.Object,
            NullLogger<FetchJobPostingJunction>.Instance
        );

        var input = new AddJobInput { UserId = "alice", Url = "https://example.com/job" };

        var result = await junction.Run(input);

        result.PastedTitle.Should().Be("Untitled");
        result.PastedCompany.Should().Be("Unknown");
        result.PastedDescription.Should().Be("");
    }
}
