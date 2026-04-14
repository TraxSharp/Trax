namespace Trax.Samples.JobHunt.Providers.Scraper;

public record ScrapeResult
{
    public string? Title { get; init; }
    public string? Company { get; init; }
    public string? Description { get; init; }
}
