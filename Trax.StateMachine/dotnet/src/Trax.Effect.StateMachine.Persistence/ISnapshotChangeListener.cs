namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Hears that a state-machine row was written. Each change names the row by its <c>row_id</c> only, as a hint: the
/// consumer re-reads the row before anything is sent on, so a forged or repeated hint moves nothing. When the
/// listener may have missed hints (it has just started listening, it reconnected after losing its session, or a
/// reader fell too far behind) it says <see cref="SnapshotChange.Reload"/> instead, and the reader re-reads every
/// row it cares about.
/// </summary>
/// <remarks>
/// <para>One per host, registered by <c>AddStateMachines</c>, and the only reader of the database's notices:
/// on Postgres it holds one listening session (<see cref="ApplicationName"/>) whatever the number of readers, and on
/// SQLite and InMemory (and on Postgres with <see cref="StateMachineChangeFeed.Poll"/>) it polls the watched rows by
/// revision, while it has a reader. Nothing is opened until the first
/// reader starts reading.</para>
/// <para>Readers inside Trax only. <see cref="SnapshotWatchHub"/> is the one that matters: it turns these hints
/// into re-read rows for the subscriptions that watch them.</para>
/// </remarks>
internal interface ISnapshotChangeListener
{
    /// <summary>The Postgres notification channel the snapshot table's writes are announced on.</summary>
    public const string Channel = "trax_snapshot_changed";

    /// <summary>The <c>application_name</c> of the listening session on Postgres.</summary>
    public const string ApplicationName = "trax_snapshot_listener";

    /// <summary>
    /// The changes as they are heard, until <paramref name="cancellationToken"/> fires. The first item is always a
    /// <see cref="SnapshotChange.Reload"/>, sent once the listener is listening: every write committed after it is
    /// heard, and anything before it must be read. A <see cref="SnapshotChange.Reload"/> follows every reconnect.
    /// </summary>
    IAsyncEnumerable<SnapshotChange> ChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The changes as <see cref="ChangesAsync(CancellationToken)"/> hands them, to a reader that says which rows it
    /// watches: <paramref name="watched"/> answers them whenever the listener asks (null, or a null answer: every
    /// row). A listener that polls reads only those rows (every row of the machines named, when every row is watched);
    /// one that listens hands every notice on, and the reader drops what it does not watch.
    /// </summary>
    IAsyncEnumerable<SnapshotChange> ChangesAsync(
        CancellationToken cancellationToken,
        Func<SnapshotWatchScope?>? watched
    );
}

/// <summary>
/// The rows a reader of <see cref="ISnapshotChangeListener"/> watches: <see cref="Rows"/>, each one on its own, and
/// every row besides when <see cref="EveryRow"/> is set, of <see cref="Machines"/> only when it names them. A reader
/// watching every row still names the rows it watches one by one, so a polling listener can report the delete of one
/// that joined since its last poll: a poll of every row knows only the rows it read before.
/// </summary>
/// <param name="Rows">The rows watched one by one.</param>
/// <param name="EveryRow">Whether every row is watched as well.</param>
/// <param name="Machines">
/// With <paramref name="EveryRow"/>, the machines whose rows are watched; null for every machine.
/// </param>
internal sealed record SnapshotWatchScope(
    IReadOnlyCollection<long> Rows,
    bool EveryRow = false,
    IReadOnlyCollection<string>? Machines = null
);

/// <summary>What <see cref="ISnapshotChangeListener"/> hands its readers.</summary>
internal abstract record SnapshotChange
{
    private SnapshotChange() { }

    /// <summary>The row <paramref name="RowId"/> was inserted, updated or deleted. A hint, never the row.</summary>
    public sealed record Row(long RowId) : SnapshotChange;

    /// <summary>Hints may have been missed: re-read every row you watch.</summary>
    public sealed record Reload : SnapshotChange
    {
        /// <summary>The one instance.</summary>
        public static readonly Reload Instance = new();
    }
}
