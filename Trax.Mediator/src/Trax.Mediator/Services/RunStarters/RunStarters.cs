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
        if (RunStarter.Hash(principal.GetCurrentPrincipalId()) is not { } starter)
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
}
