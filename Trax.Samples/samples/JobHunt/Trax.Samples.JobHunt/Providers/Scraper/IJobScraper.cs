namespace Trax.Samples.JobHunt.Providers.Scraper;

public interface IJobScraper
{
    Task<ScrapeResult> ScrapeAsync(Uri url, CancellationToken ct = default);
}
