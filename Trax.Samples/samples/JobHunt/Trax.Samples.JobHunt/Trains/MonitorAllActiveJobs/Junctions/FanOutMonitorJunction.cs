using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Trains.MonitorJob;

namespace Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs.Junctions;

public class FanOutMonitorJunction(
    JobHuntDbContext db,
    ITrainBus trainBus,
    ILogger<FanOutMonitorJunction> logger
) : Junction<MonitorAllActiveJobsInput, MonitorAllActiveJobsOutput>
{
    public override async Task<MonitorAllActiveJobsOutput> Run(MonitorAllActiveJobsInput input)
    {
        var activeJobIds = await db
            .Jobs.Where(j => j.Status == JobStatus.Active && j.Url != null)
            .Select(j => j.Id)
            .ToListAsync();

        logger.LogInformation("Monitoring {Count} active jobs with URLs", activeJobIds.Count);

        var changed = 0;
        var closed = 0;

        foreach (var jobId in activeJobIds)
        {
            var result = await trainBus.RunAsync<MonitorJobOutput>(
                new MonitorJobInput { JobId = jobId }
            );

            if (result.Changed)
                changed++;
            if (result.Closed)
                closed++;
        }

        return new MonitorAllActiveJobsOutput
        {
            JobsChecked = activeJobIds.Count,
            JobsChanged = changed,
            JobsClosed = closed,
        };
    }
}
