using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.ListJobs.Junctions;

public class LoadJobsJunction(JobHuntDbContext db) : Junction<ListJobsInput, ListJobsOutput>
{
    public override async Task<ListJobsOutput> Run(ListJobsInput input)
    {
        var query = db.Jobs.Where(j => j.UserId == input.UserId);

        if (input.Status.HasValue)
            query = query.Where(j => j.Status == input.Status.Value);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobSummary
            {
                Id = j.Id,
                Title = j.Title,
                Company = j.Company,
                Url = j.Url,
                Status = j.Status.ToString(),
                CreatedAt = j.CreatedAt,
            })
            .ToListAsync();

        return new ListJobsOutput { Jobs = jobs };
    }
}
