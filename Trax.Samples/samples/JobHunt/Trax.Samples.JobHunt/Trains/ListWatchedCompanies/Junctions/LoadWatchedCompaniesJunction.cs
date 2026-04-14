using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.ListWatchedCompanies.Junctions;

public class LoadWatchedCompaniesJunction(JobHuntDbContext db)
    : Junction<ListWatchedCompaniesInput, ListWatchedCompaniesOutput>
{
    public override async Task<ListWatchedCompaniesOutput> Run(ListWatchedCompaniesInput input)
    {
        var companies = await db
            .WatchedCompanies.Where(w => w.UserId == input.UserId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WatchedCompanySummary
            {
                Id = w.Id,
                CompanyName = w.CompanyName,
                CareersUrl = w.CareersUrl,
                LastCheckedAt = w.LastCheckedAt,
            })
            .ToListAsync();

        return new ListWatchedCompaniesOutput { Companies = companies };
    }
}
