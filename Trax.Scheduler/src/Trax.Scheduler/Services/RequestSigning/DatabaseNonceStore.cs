using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.RunnerNonce;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// An <see cref="INonceStore"/> in the <c>runner_nonce</c> table, which every runner instance on the
/// same database shares. The default store for a runner whose host has a relational data provider
/// (see scheduler/0009). The table and its <see cref="RunnerNonce"/> model ship in Trax.Effect, and
/// this store reaches them through <c>IDataContext.RunnerNonces</c> (docs/0036).
/// </summary>
/// <remarks>
/// The primary key on <c>nonce</c> is what makes a nonce accepted once. The store inserts the row,
/// and when the key refuses it, takes the existing row over only if it has expired. Both steps are
/// single statements the database serializes on that key, so two instances racing one nonce accept
/// it once: the loser's insert conflicts, and its takeover matches nothing because the winner's row
/// has not expired. Only the conflict <see cref="ISqlDialect.IsUniqueViolation"/> recognises reads as
/// a replay; any other failed save throws.
/// </remarks>
internal sealed class DatabaseNonceStore(IDataContextProviderFactory contexts, ISqlDialect dialect)
    : INonceStore
{
    /// <summary>How many recorded nonces pass between deletes of the expired ones.</summary>
    internal const int SweepEvery = 256;

    private long _recordedSinceSweep;

    public async ValueTask<bool> TryRecordAsync(
        string nonce,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var _ = context;

        var recorded = await Record(context, nonce, expiresAt, now, cancellationToken);

        if (recorded && Interlocked.Increment(ref _recordedSinceSweep) % SweepEvery == 0)
            await context
                .RunnerNonces.Where(n => n.ExpiresAt < now)
                .ExecuteDeleteAsync(cancellationToken);

        return recorded;
    }

    private async Task<bool> Record(
        IDataContext context,
        string nonce,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        context.RunnerNonces.Add(new RunnerNonce { Nonce = nonce, ExpiresAt = expiresAt });
        try
        {
            await context.SaveChanges(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (dialect.IsUniqueViolation(exception))
        {
            // The nonce is recorded. A record past its expiry refuses nothing, so it is taken
            // over, which is the same as it having been swept first; a live one is a replay.
            context.Reset();
            var takenOver = await context
                .RunnerNonces.Where(n => n.Nonce == nonce && n.ExpiresAt < now)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(n => n.ExpiresAt, expiresAt),
                    cancellationToken
                );
            return takenOver == 1;
        }
    }
}
