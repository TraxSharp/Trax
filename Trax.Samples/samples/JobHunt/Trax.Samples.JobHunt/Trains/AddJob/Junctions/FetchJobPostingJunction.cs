using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Providers.Scraper;

namespace Trax.Samples.JobHunt.Trains.AddJob.Junctions;

/// <summary>
/// If the input has a URL (and no pasted fields), scrapes the page and
/// populates the pasted fields from the scraped data. If pasted fields
/// are already set, this junction is a pass-through.
/// </summary>
public class FetchJobPostingJunction(IJobScraper scraper, ILogger<FetchJobPostingJunction> logger)
    : Junction<AddJobInput, AddJobInput>
{
    public override async Task<AddJobInput> Run(AddJobInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.PastedTitle))
            return input;

        if (string.IsNullOrWhiteSpace(input.Url))
            return input;

        logger.LogInformation("Scraping job posting from {Url}", input.Url);
        var result = await scraper.ScrapeAsync(new Uri(input.Url));

        return input with
        {
            PastedTitle = result.Title ?? "Untitled",
            PastedCompany = result.Company ?? "Unknown",
            PastedDescription = result.Description ?? "",
        };
    }
}
