using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Providers.Scraper;

namespace Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

public class RefetchAndHashJunction(
    JobHuntDbContext db,
    IJobScraper scraper,
    ILogger<RefetchAndHashJunction> logger
) : Junction<MonitorJobInput, MonitorJobContext>
{
    public override async Task<MonitorJobContext> Run(MonitorJobInput input)
    {
        var job =
            await db.Jobs.FirstOrDefaultAsync(j => j.Id == input.JobId)
            ?? throw new InvalidOperationException($"Job {input.JobId} not found.");

        if (string.IsNullOrWhiteSpace(job.Url))
        {
            // No URL to monitor, just return as unchanged
            return new MonitorJobContext
            {
                JobId = job.Id,
                JobUrl = "",
                FetchedContent = job.RawDescription,
                ContentHash = ComputeHash(job.RawDescription),
                Closed = false,
            };
        }

        try
        {
            var result = await scraper.ScrapeAsync(new Uri(job.Url));
            var content = result.Description ?? "";
            logger.LogInformation("Refetched job {JobId} from {Url}", job.Id, job.Url);

            return new MonitorJobContext
            {
                JobId = job.Id,
                JobUrl = job.Url,
                FetchedContent = content,
                ContentHash = ComputeHash(content),
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("Job {JobId} returned 404, marking as closed", job.Id);
            return new MonitorJobContext
            {
                JobId = job.Id,
                JobUrl = job.Url,
                FetchedContent = "",
                ContentHash = "",
                Closed = true,
            };
        }
    }

    internal static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}
