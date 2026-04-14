using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.CreateApplication.Junctions;

public class PersistApplicationJunction(
    JobHuntDbContext db,
    ILogger<PersistApplicationJunction> logger
) : Junction<CreateApplicationInput, CreateApplicationOutput>
{
    public override async Task<CreateApplicationOutput> Run(CreateApplicationInput input)
    {
        var now = DateTime.UtcNow;

        var application = new Application
        {
            Id = Guid.NewGuid(),
            JobId = input.JobId,
            UserId = input.UserId,
            Status = ApplicationStatus.Drafted,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Applications.Add(application);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created application {ApplicationId} for job {JobId}",
            application.Id,
            input.JobId
        );

        return new CreateApplicationOutput
        {
            ApplicationId = application.Id,
            Status = application.Status.ToString(),
        };
    }
}
