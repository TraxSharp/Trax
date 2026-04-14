using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.AddJob.Junctions;

public class PersistJobJunction(JobHuntDbContext db, ILogger<PersistJobJunction> logger)
    : Junction<AddJobInput, AddJobOutput>
{
    public override async Task<AddJobOutput> Run(AddJobInput input)
    {
        var now = DateTime.UtcNow;

        var job = new Job
        {
            Id = Guid.NewGuid(),
            UserId = input.UserId,
            Title = input.PastedTitle ?? string.Empty,
            Company = input.PastedCompany ?? string.Empty,
            Url = input.Url,
            RawDescription = input.PastedDescription ?? string.Empty,
            Status = JobStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Persisted job {JobId} for user {UserId}: {Title} at {Company}",
            job.Id,
            input.UserId,
            job.Title,
            job.Company
        );

        return new AddJobOutput
        {
            JobId = job.Id,
            UserId = input.UserId,
            Title = job.Title,
            Company = job.Company,
        };
    }
}
