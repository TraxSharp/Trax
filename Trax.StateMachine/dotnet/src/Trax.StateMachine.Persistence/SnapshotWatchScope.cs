namespace Trax.StateMachine.Persistence;

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
