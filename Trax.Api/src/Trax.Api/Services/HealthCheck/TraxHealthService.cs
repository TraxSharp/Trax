using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.Api.Services.HealthCheck;

public class TraxHealthService(IDataContextProviderFactory dataContextFactory) : ITraxHealthService
{
    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var cutoff = DateTime.UtcNow.AddHours(-1);

        // Single round-trip: project all four counts from a constant source row.
        var counts = await db
            .Metadatas.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                InProgress = g.Count(m => m.TrainState == TrainState.InProgress),
                FailedLastHour = g.Count(m =>
                    m.TrainState == TrainState.Failed && m.EndTime > cutoff
                ),
            })
            .FirstOrDefaultAsync(ct);

        var queueDepth = await db
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.Status == WorkQueueStatus.Queued, ct);

        var deadLetters = await db
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, ct);

        var inProgress = counts?.InProgress ?? 0;
        var failedLastHour = counts?.FailedLastHour ?? 0;
        var isDegraded = deadLetters > 0 || failedLastHour > 10;

        return new HealthStatus(
            Status: isDegraded ? "Degraded" : "Healthy",
            Description: isDegraded
                ? "Elevated failures or unresolved dead letters"
                : "All systems operational",
            QueueDepth: queueDepth,
            InProgress: inProgress,
            FailedLastHour: failedLastHour,
            DeadLetters: deadLetters
        );
    }
}
