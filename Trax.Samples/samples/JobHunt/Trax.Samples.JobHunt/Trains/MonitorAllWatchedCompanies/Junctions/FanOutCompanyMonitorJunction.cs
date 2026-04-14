using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Providers.Scraper;

namespace Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies.Junctions;

public class FanOutCompanyMonitorJunction(
    JobHuntDbContext db,
    IJobScraper scraper,
    ILogger<FanOutCompanyMonitorJunction> logger
) : Junction<MonitorAllWatchedCompaniesInput, MonitorAllWatchedCompaniesOutput>
{
    public override async Task<MonitorAllWatchedCompaniesOutput> Run(
        MonitorAllWatchedCompaniesInput input
    )
    {
        var companies = await db.WatchedCompanies.ToListAsync();

        logger.LogInformation("Checking {Count} watched companies", companies.Count);

        foreach (var company in companies)
        {
            try
            {
                var result = await scraper.ScrapeAsync(new Uri(company.CareersUrl));
                var content = result.Description ?? "";
                var fingerprint = Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(content))
                );

                company.LastCheckedAt = DateTime.UtcNow;
                company.LastFingerprint = fingerprint;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to check {Company} at {Url}",
                    company.CompanyName,
                    company.CareersUrl
                );
            }
        }

        await db.SaveChangesAsync();

        return new MonitorAllWatchedCompaniesOutput { CompaniesChecked = companies.Count };
    }
}
