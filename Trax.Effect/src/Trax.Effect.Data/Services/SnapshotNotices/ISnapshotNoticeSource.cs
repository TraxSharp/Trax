namespace Trax.Effect.Data.Services.SnapshotNotices;

/// <summary>
/// A data provider's notice that a state-machine row was written: inserted, updated or deleted, from any host,
/// once the writing transaction commits. Each notice carries the row's <c>row_id</c> as text and nothing else.
/// </summary>
/// <remarks>
/// <para><c>UsePostgres</c> registers one: migration 078's trigger on <c>trax.snapshot_draft</c> sends
/// <c>NOTIFY trax_snapshot_changed, '&lt;row_id&gt;'</c> for every row it writes, unless the database turns it off
/// (migration 080). SQLite and InMemory register none,
/// and the state-machine change listener polls the table instead.</para>
///
/// <para>Infrastructure for <c>Trax.StateMachine.Persistence</c>, whose change listener holds the host's one
/// subscription; a host does not implement or call it.</para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal interface ISnapshotNoticeSource
{
    /// <summary>
    /// Starts listening. The returned subscription hears every notice sent after this completes, until it is
    /// disposed.
    /// </summary>
    Task<ISnapshotNoticeSubscription> SubscribeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads what decides whether a write sends a notice (migration 080): the trigger's state, the database's own
    /// <c>trax.snapshot_notify</c>, the value a new session of this host starts with, and the roles that turn it off
    /// for themselves. The trigger reads the setting in the session that writes, so only the database's value is
    /// the same for every host.
    /// </summary>
    Task<SnapshotNoticeSwitch> ReadSwitchAsync(CancellationToken cancellationToken);
}
