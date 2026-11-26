namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Whether the hosts sharing the database are drained, and what is still busy. Returned by
/// <see cref="IOperationsService.GetDrainAsync"/>; the console's scheduler page and the GraphQL
/// <c>operations.config.drain</c> query both read it.
/// </summary>
/// <param name="Drained">
/// Whether a drain is in force: no host dispatches, queues a scheduled run or claims a job until
/// <paramref name="Until"/>.
/// </param>
/// <param name="Until">When the drain lapses, in UTC; null when none is in force.</param>
/// <param name="StartedAt">When the drain was set, in UTC; null when none is in force.</param>
/// <param name="StartedBy">
/// <c>deploy</c> for a drain a deploy set, or the operator who set it; null when none is in force.
/// </param>
/// <param name="Busy">
/// The work a drain waits on, at most 50 items, runs first. Read whether or not a drain is in
/// force, so an operator can see what one would wait on.
/// </param>
/// <param name="BusyTotal">How many items are busy in all, past the list's cap too.</param>
public record DrainStatus(
    bool Drained,
    DateTime? Until,
    DateTime? StartedAt,
    string? StartedBy,
    IReadOnlyList<DrainBusyWork> Busy,
    int BusyTotal
);
