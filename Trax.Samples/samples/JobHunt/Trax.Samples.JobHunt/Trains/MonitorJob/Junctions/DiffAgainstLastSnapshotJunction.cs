using Microsoft.EntityFrameworkCore;
using Trax.Core.Junction;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Trains.MonitorJob.Junctions;

public class DiffAgainstLastSnapshotJunction(JobHuntDbContext db)
    : Junction<MonitorJobContext, MonitorJobContext>
{
    public override async Task<MonitorJobContext> Run(MonitorJobContext ctx)
    {
        if (ctx.Closed)
            return ctx with { Changed = true, DiffSummary = "Job posting is no longer available." };

        var lastSnapshot = await db
            .JobSnapshots.Where(s => s.JobId == ctx.JobId)
            .OrderByDescending(s => s.FetchedAt)
            .FirstOrDefaultAsync();

        if (lastSnapshot is null)
            return ctx with { Changed = false, PreviousHash = null };

        var changed = lastSnapshot.ContentHash != ctx.ContentHash;

        return ctx with
        {
            Changed = changed,
            PreviousHash = lastSnapshot.ContentHash,
            DiffSummary = changed ? "Content has changed since last check." : null,
        };
    }
}
