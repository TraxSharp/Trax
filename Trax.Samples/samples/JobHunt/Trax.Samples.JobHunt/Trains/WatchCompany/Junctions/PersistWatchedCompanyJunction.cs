using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.WatchCompany.Junctions;

public class PersistWatchedCompanyJunction(
    JobHuntDbContext db,
    ILogger<PersistWatchedCompanyJunction> logger
) : Junction<WatchCompanyInput, WatchCompanyOutput>
{
    public override async Task<WatchCompanyOutput> Run(WatchCompanyInput input)
    {
        var entry = new WatchedCompany
        {
            Id = Guid.NewGuid(),
            UserId = input.UserId,
            CompanyName = input.CompanyName,
            CareersUrl = input.CareersUrl,
            CreatedAt = DateTime.UtcNow,
        };

        db.WatchedCompanies.Add(entry);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Now watching {Company} at {Url}",
            input.CompanyName,
            input.CareersUrl
        );

        return new WatchCompanyOutput
        {
            WatchedCompanyId = entry.Id,
            CompanyName = entry.CompanyName,
        };
    }
}
