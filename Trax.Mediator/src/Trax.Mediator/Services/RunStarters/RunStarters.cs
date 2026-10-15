using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Mediator.Services.Principal;

namespace Trax.Mediator.Services.RunStarters;

/// <inheritdoc />
internal sealed class RunStarters(
    IDataContextProviderFactory contexts,
    ICurrentPrincipalProvider principal
) : IRunStarters
{
    /// <inheritdoc />
    public async Task<bool> StartedByCurrentCallerAsync(
        string runId,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(runId);
        if (RunStarter.OfPrincipal(principal.GetCurrentPrincipalId()) is not { } starter)
            return false;

        using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db
                .Metadatas.AsNoTracking()
                .AnyAsync(m => m.ExternalId == runId && m.StarterHash == starter, ct)
                .ConfigureAwait(false)
            || await db
                .WorkQueues.AsNoTracking()
                .AnyAsync(
                    w =>
                        w.ExternalId == runId
                        && w.Status == WorkQueueStatus.Queued
                        && w.StarterHash == starter,
                    ct
                )
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long?> LatestExecutionStartedByCurrentCallerAsync(
        string runId,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(runId);
        if (RunStarter.OfPrincipal(principal.GetCurrentPrincipalId()) is not { } starter)
            return null;

        using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == runId && m.StarterHash == starter)
            .OrderByDescending(m => m.Id)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
