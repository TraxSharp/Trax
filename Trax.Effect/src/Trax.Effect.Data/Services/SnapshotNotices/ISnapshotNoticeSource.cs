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
/// <para>Infrastructure for <c>Trax.Effect.StateMachine.Persistence</c>, whose change listener holds the host's one
/// subscription; a host does not implement or call it.</para>
/// </remarks>
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

/// <summary>One listening session opened by <see cref="ISnapshotNoticeSource.SubscribeAsync"/>.</summary>
internal interface ISnapshotNoticeSubscription : IAsyncDisposable
{
    /// <summary>
    /// Completes with the payload of the next notice, exactly as it was sent (a forged one included), or with null
    /// when <paramref name="idle"/> passes without one. Throws when the session is lost, for example when its
    /// database connection is closed; dispose it and subscribe again.
    /// </summary>
    Task<string?> NextAsync(TimeSpan idle, CancellationToken cancellationToken);

    /// <summary>
    /// How full the database's notification queue is, from 0 to 1 (<c>pg_notification_queue_usage()</c>), read on
    /// this session. Call it only between <see cref="NextAsync"/> calls.
    /// </summary>
    Task<double> QueueUsageAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What decides whether a write to <c>trax.snapshot_draft</c> sends a notice, as one host reads it.
/// </summary>
/// <param name="TriggerState">
/// The trigger's <c>pg_trigger.tgenabled</c>: <c>O</c> fires for ordinary sessions, <c>A</c> always, <c>R</c> for
/// replication sessions only, <c>D</c> never; null when there is no trigger.
/// </param>
/// <param name="TriggerHasCondition">Whether the trigger reads the switch (080); one without a condition always fires.</param>
/// <param name="DatabaseValue">The database's own setting (<c>ALTER DATABASE ... SET</c>), or null.</param>
/// <param name="SessionValue">The setting a new session of this host reads, or null.</param>
/// <param name="SessionIsReplica">Whether this host's session starts with <c>session_replication_role = replica</c>.</param>
/// <param name="RolesTurningOff">The roles whose own setting turns the notices off in this database.</param>
internal sealed record SnapshotNoticeSwitch(
    string? TriggerState,
    bool TriggerHasCondition,
    string? DatabaseValue,
    string? SessionValue,
    bool SessionIsReplica,
    IReadOnlyList<string> RolesTurningOff
)
{
    /// <summary>The setting's name.</summary>
    public const string Setting = "trax.snapshot_notify";

    /// <summary>Whether <paramref name="value"/> turns the notices off: <c>off</c> in any case, as the trigger reads it.</summary>
    public static bool IsOff(string? value) =>
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="value"/> is unset, <c>on</c> or <c>off</c>, in any case.</summary>
    public static bool IsKnown(string? value) =>
        value is null
        || IsOff(value)
        || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the trigger fires for an ordinary session.</summary>
    public bool TriggerFires => TriggerState is "O" or "A";

    /// <summary>Whether a write from a session that takes the database's setting sends a notice.</summary>
    public bool DatabaseSends => TriggerFires && (!TriggerHasCondition || !IsOff(DatabaseValue));

    /// <summary>Whether a write from a new session of this host sends a notice.</summary>
    public bool SessionSends =>
        (TriggerState is "A" || (TriggerState is "O" && !SessionIsReplica))
        && (!TriggerHasCondition || !IsOff(SessionValue));
}
