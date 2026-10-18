namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Tells this host's <see cref="SnapshotChangeListener"/> that a draft was written and committed here, so a
/// listener that polls (SQLite, InMemory, Postgres in poll mode) reads at once instead of at its next poll. A write
/// another host made is still found by the poll. Raised after the commit, never inside a transaction: a poll woken
/// before the commit would read the old revision and wait a whole interval for the new one.
/// </summary>
internal sealed class SnapshotWriteSignal
{
    // One slot: writes that land while a poll is waiting or running coalesce into the one poll that follows them.
    private readonly SemaphoreSlim _written = new(0, 1);

    /// <summary>A draft was written and its transaction, if any, committed.</summary>
    public void Written()
    {
        try
        {
            _written.Release();
        }
        catch (SemaphoreFullException)
        {
            // A poll is already owed.
        }
    }

    /// <summary>Completes when a write is signalled after this call began, or when <paramref name="delay"/> passes.</summary>
    /// <returns>False when <paramref name="stoppingToken"/> was cancelled.</returns>
    public async Task<bool> WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await _written.WaitAsync(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
