using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.ListApplications.Junctions;

public class LoadApplicationsJunction(JobHuntDbContext db)
    : Junction<ListApplicationsInput, ListApplicationsOutput>
{
    public override async Task<ListApplicationsOutput> Run(ListApplicationsInput input)
    {
        var applications = await db
            .Applications.Where(a => a.UserId == input.UserId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ApplicationSummary
            {
                Id = a.Id,
                JobId = a.JobId,
                Status = a.Status.ToString(),
                CreatedAt = a.CreatedAt,
            })
            .ToListAsync();

        return new ListApplicationsOutput { Applications = applications };
    }
}
