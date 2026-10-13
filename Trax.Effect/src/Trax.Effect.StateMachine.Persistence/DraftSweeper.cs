using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Deletes users' drafts idle past <see cref="StateMachineOptions.DraftTtl"/> with no timer pending and no invoked
/// run, and those older than <see cref="StateMachineOptions.MaxDraftLifetime"/> whatever their timers or runs say,
/// on every host that registers machines, every <see cref="StateMachineOptions.DraftSweepInterval"/>. A row the server
/// put in its state (an effect, an invoked train's outcome, a gated event) is kept, however idle or old: that is the
/// record of what the server did, and the host decides how long it stays; a row that does not record who put it there
/// is kept while its state is one only the server puts it in. A draft past the lifetime in a state that invokes a train
/// has its run cancelled first. A read never deletes a draft; this does. It reads a batch of expired drafts of the
/// machines this host registers, then deletes each only while it is still the row, revision and run it read, still
/// expired and with no effect pending on it, so a draft written after the read is kept. A draft an effect is pending
/// on is skipped until the effect is recorded or ends.
/// A system-owned instance is never read. With neither a TTL nor a lifetime it does nothing; <c>DraftTtl = null</c>
/// alone still leaves the lifetime.
/// </summary>
internal sealed class DraftSweeper(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopes,
    StateMachineOptions options,
    ILogger<DraftSweeper>? logger = null
) : BackgroundService
{
    /// <summary>How many expired drafts one pass reads at most; a full batch is followed by another pass at once.</summary>
    internal const int BatchSize = 500;

    // The user-owned machines this host registers, by name: only their drafts are swept here, because only their
    // effect claims can be named here.
    private readonly Lazy<IReadOnlyDictionary<string, IMachineInternals>> _machines = new(() =>
        machines
            .OfType<IMachineInternals>()
            .Where(m => !m.SystemOwned)
            .Select(m => (Name: ((IMachine)m).Name, Machine: m))
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Machine, StringComparer.Ordinal)
    );

    // Every state of those machines that neither the TTL nor the lifetime deletes in, as DraftExpiry lists them.
    private readonly Lazy<IReadOnlyCollection<string>> _kept = new(() =>
        machines
            .OfType<IMachineInternals>()
            .Where(m => !m.SystemOwned)
            .SelectMany(m =>
                m.KeptFromSweep.Select(state => DraftExpiry.Key(((IMachine)m).Name, state))
            )
            .Distinct(StringComparer.Ordinal)
            .ToList()
    );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.DraftTtl is null && options.MaxDraftLifetime is null)
            return;

        using var timer = new PeriodicTimer(options.DraftSweepInterval);
        do
        {
            try
            {
                while (await SweepOnce(stoppingToken) >= BatchSize)
                    stoppingToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A sweep that fails (the database is away) is tried again on the next tick.
                logger?.LogWarning(ex, "The draft sweep failed; it runs again on its next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One pass: reads up to <see cref="BatchSize"/> expired drafts and deletes each one still expired at the
    /// revision read. Returns how many it deleted.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read; a delete that has started finishes.</param>
    /// <param name="afterRead">Runs between the read and the deletes, with the ids read.</param>
    internal async Task<int> SweepOnce(
        CancellationToken cancellationToken = default,
        Func<IReadOnlyList<Guid>, Task>? afterRead = null
    )
    {
        if (
            (options.DraftTtl is null && options.MaxDraftLifetime is null)
            || _machines.Value.Count == 0
        )
            return 0;

        var now = DateTimeOffset.UtcNow;
        var expiredBefore = new DraftExpiry(
            now - options.DraftTtl,
            now - options.MaxDraftLifetime,
            _kept.Value
        );
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IDataContext>();
        var store = new EfSnapshotStore(context, services.GetService<ISqlDialect>());

        var expired = await store.ReadExpired(
            _machines.Value.Keys.ToList(),
            expiredBefore,
            BatchSize,
            cancellationToken
        );
        if (afterRead is not null)
            await afterRead(expired.Select(d => d.Id).ToList());

        var runs = services.GetService<IInvokedRunCancellation>();
        var deleted = 0;
        foreach (var draft in expired)
            if (await Delete(store, runs, draft, expiredBefore))
                deleted++;
        return deleted;
    }

    // The row holds any effect pending on it, and the expiry query passes such a row over, so nothing about an effect is
    // held beside the row but a claim the earlier ledger recorded before the upgrade: one in flight within its lease, or
    // a completed one whose draft is still in its effect's state, keeps the draft for a later pass. A run the draft holds
    // is cancelled next, as a discard cancels it. The delete is conditional on the row as read, and the draft's ledger
    // claims go in its transaction, on a token nothing cancels.
    private async Task<bool> Delete(
        EfSnapshotStore store,
        IInvokedRunCancellation? runs,
        ExpiredDraft draft,
        DraftExpiry expiredBefore
    )
    {
        var keys = _machines.Value.TryGetValue(draft.Machine, out var machine)
            ? machine.EffectKeysWithStates(draft.UserKey, draft.Id).ToList()
            : [];
        var keyList = keys.Select(k => k.Key).ToList();
        if (
            DraftClaimHold.LedgerHolds(
                await store.LedgerClaims(keyList, CancellationToken.None),
                keys,
                draft.State
            )
        )
            return false;

        // With nothing to cancel the run through, the draft is kept rather than its run stranded.
        if (draft.InvokeToken is { } token)
        {
            if (runs is null)
                return false;
            await runs.Cancel(token, CancellationToken.None);
        }
        return await store.DeleteExpired(draft, expiredBefore, () => store.ReleaseLedger(keyList));
    }
}
