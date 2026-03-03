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

        var queueDepth = await db
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.Status == WorkQueueStatus.Queued, ct);

        var inProgress = await db
            .Metadatas.AsNoTracking()
            .CountAsync(m => m.TrainState == TrainState.InProgress, ct);

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var failedLastHour = await db
            .Metadatas.AsNoTracking()
            .CountAsync(m => m.TrainState == TrainState.Failed && m.EndTime > cutoff, ct);

        var deadLetters = await db
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, ct);

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
