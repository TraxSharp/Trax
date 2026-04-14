using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

public class PersistSnapshotAndUpdateJobJunction(
    JobHuntDbContext db,
    ILogger<PersistSnapshotAndUpdateJobJunction> logger
) : Junction<MonitorJobContext, MonitorJobOutput>
{
    public override async Task<MonitorJobOutput> Run(MonitorJobContext ctx)
    {
        var snapshot = new JobSnapshot
        {
            Id = Guid.NewGuid(),
            JobId = ctx.JobId,
            FetchedAt = DateTime.UtcNow,
            ContentHash = ctx.ContentHash,
            RawContent = ctx.FetchedContent,
            DiffFromPrevious = ctx.DiffSummary,
        };

        db.JobSnapshots.Add(snapshot);

        if (ctx.Closed)
        {
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == ctx.JobId);
            if (job is not null)
            {
                job.Status = JobStatus.Closed;
                job.UpdatedAt = DateTime.UtcNow;
                logger.LogInformation("Closed job {JobId} after 404", ctx.JobId);
            }
        }

        await db.SaveChangesAsync();

        return new MonitorJobOutput
        {
            Changed = ctx.Changed,
            Closed = ctx.Closed,
            DiffSummary = ctx.DiffSummary,
        };
    }
}
