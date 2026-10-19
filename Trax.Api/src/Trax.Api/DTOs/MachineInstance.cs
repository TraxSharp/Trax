using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// One state-machine instance in the operator's list: which machine, who owns it, what state it
/// is in and when it got there.
/// </summary>
/// <remarks>
/// It carries no context and no owner key, on the list or on the detail
/// (<see cref="MachineInstanceDetail"/>). The context is an untyped JSON object, so nothing can
/// mask its sensitive parts, and no operator view names the user behind a row. Whether the
/// instance waits on a train run it invoked is a flag, not the run's token.
/// </remarks>
/// <param name="RowId">
/// The row's key. Pass it to <c>machineInstance</c> to read a user's draft, since several users
/// can each hold a draft under one id.
/// </param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <c>null</c> for one written before Trax recorded it.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
public record MachineInstance(
    long RowId,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    string State,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasLiveInvokedRun
)
{
    /// <summary>The instance's revision: 1 when it was created, one more for every write since.</summary>
    public long? Revision { get; init; }

    /// <summary>When the timer of its state fires, or <c>null</c> when that state has no timer.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>True when <see cref="DueAt"/> is past by more than the host's timer poll: the timer has not fired.</summary>
    public bool Overdue { get; init; }

    /// <summary>The run its state waits on and where that run is, or <c>null</c> when it waits on none.</summary>
    public MachineInstanceWaitingRun? WaitingOn { get; init; }

    /// <summary>
    /// When it entered its state, as far as Trax records it: until a transition log is kept, when it last
    /// changed (<c>updatedAt</c>). Show it as "last changed".
    /// </summary>
    public DateTimeOffset InStateSince { get; init; }

    /// <summary>True when it cannot move on its own: <see cref="Overdue"/>, or its run failed or was cancelled.</summary>
    public bool Stuck { get; init; }

    /// <summary>The list item for <paramref name="record"/>.</summary>
    internal static MachineInstance From(MachineInstanceRecord record) =>
        new(
            record.RowId,
            record.Machine,
            record.OwnerKind,
            record.Id,
            record.State,
            record.Version,
            record.CreatedAt,
            record.UpdatedAt,
            record.HasLiveInvokedRun
        )
        {
            Revision = record.Revision,
            DueAt = record.DueAt,
            Overdue = record.Overdue,
            WaitingOn = MachineInstanceWaitingRun.From(record.WaitingOn),
            InStateSince = record.InStateSince,
            Stuck = record.Stuck,
        };
}

/// <summary>
/// The train run a state-machine instance's state waits on: which run, and where it is. Never the
/// instance's invoke token.
/// </summary>
/// <param name="ExecutionId">The run's id, or <c>null</c> while it is queued and has no run yet.</param>
/// <param name="TrainState">
/// Where the run is: its state once dispatched, <c>PENDING</c> while its entry is queued,
/// <c>CANCELLED</c> when the entry was cancelled before it started; <c>null</c> when neither is found.
/// </param>
/// <param name="WorkQueueEntryId">The run's work queue entry, or <c>null</c> when none is found.</param>
public record MachineInstanceWaitingRun(
    long? ExecutionId,
    TrainState? TrainState,
    long? WorkQueueEntryId
)
{
    internal static MachineInstanceWaitingRun? From(MachineInstanceWaitingOn? waiting) =>
        waiting is null
            ? null
            : new(waiting.ExecutionId, waiting.TrainState, waiting.WorkQueueEntryId);
}

/// <summary>
/// One event of the operator's live feed, <c>onMachineInstanceChanged</c>: a state-machine
/// instance as it was just written, or, with <see cref="Deleted"/> set, as it was last seen
/// before it was deleted or expired.
/// </summary>
/// <remarks>
/// Like the list item it carries no context and no owner key: an operator sees that an instance
/// moved and where to, never what it holds or whose it is.
/// </remarks>
/// <param name="RowId">The row's key, which names one incarnation of the instance.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <c>null</c> where that is not known.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
/// <param name="Revision">Its revision: 1 when it was created, one more for every write since.</param>
/// <param name="Deleted">True when the instance was deleted or expired; nothing follows for this row.</param>
public record MachineInstanceChange(
    long RowId,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    string State,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasLiveInvokedRun,
    long? Revision,
    bool Deleted
)
{
    /// <summary>
    /// True when writes may have been missed (the host's change listener started or reconnected, or this feed fell
    /// behind): the event names no instance, and the subscriber reads the instances it shows again.
    /// </summary>
    public bool Reload { get; init; }

    /// <summary>The feed event for <paramref name="record"/>.</summary>
    internal static MachineInstanceChange From(MachineInstanceRecord record) =>
        new(
            record.RowId,
            record.Machine,
            record.OwnerKind,
            record.Id,
            record.State,
            record.Version,
            record.CreatedAt,
            record.UpdatedAt,
            record.HasLiveInvokedRun,
            record.Revision,
            record.Deleted
        )
        {
            Reload = record.Reload,
        };
}

/// <summary>
/// One state-machine instance read on its own, by <c>operations.machineInstance</c>. It has the
/// list item's fields and the runs the instance invoked, which only this lookup reads, so a page
/// of instances never pays for a lookup per row.
/// </summary>
/// <remarks>
/// Like the list item it carries no context and no owner key, and its runs carry no input or
/// output. A system instance lists every run it invoked; a user's draft lists only its live run,
/// because a run does not record which user's draft queued it and several users can each hold a
/// draft under one id.
/// </remarks>
/// <param name="RowId">The row's key.</param>
/// <param name="Machine">The machine's id.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="Id">The instance or draft id.</param>
/// <param name="State">The state it is in.</param>
/// <param name="Version">The machine definition version it was last written under.</param>
/// <param name="CreatedAt">When it was created, or <c>null</c> for one written before Trax recorded it.</param>
/// <param name="UpdatedAt">When it was last written.</param>
/// <param name="HasLiveInvokedRun">True while its state waits on the outcome of a train run it invoked.</param>
public record MachineInstanceDetail(
    long RowId,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    string State,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasLiveInvokedRun
)
{
    /// <summary>
    /// The train runs the instance invoked, newest first, at most 50, with the one its state
    /// waits on marked <c>isLive</c>. A user's draft lists only its live run.
    /// </summary>
    public IReadOnlyList<MachineInstanceInvokedRun> InvokedRuns { get; init; } = [];

    /// <summary>True when the instance invoked more runs than <see cref="InvokedRuns"/> lists.</summary>
    public bool IsInvokedRunsCapped { get; init; }

    /// <summary>The instance's revision: 1 when it was created, one more for every write since.</summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The work queue entry of the run the state waits on while it is still queued, and so not
    /// yet in <see cref="InvokedRuns"/>; <c>null</c> otherwise.
    /// </summary>
    public long? QueuedInvokedRunEntryId { get; init; }

    /// <summary>When the timer of its state fires, or <c>null</c> when that state has no timer.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>True when <see cref="DueAt"/> is past by more than the host's timer poll: the timer has not fired.</summary>
    public bool Overdue { get; init; }

    /// <summary>The run its state waits on and where that run is, or <c>null</c> when it waits on none.</summary>
    public MachineInstanceWaitingRun? WaitingOn { get; init; }

    /// <summary>
    /// When it entered its state, as far as Trax records it: until a transition log is kept, when it last
    /// changed (<c>updatedAt</c>). Show it as "last changed".
    /// </summary>
    public DateTimeOffset InStateSince { get; init; }

    /// <summary>True when it cannot move on its own: <see cref="Overdue"/>, or its run failed or was cancelled.</summary>
    public bool Stuck { get; init; }

    /// <summary>The detail for <paramref name="record"/>, with the runs <paramref name="runs"/> lists.</summary>
    internal static MachineInstanceDetail From(
        MachineInstanceRecord record,
        MachineInstanceRuns? runs
    ) =>
        new(
            record.RowId,
            record.Machine,
            record.OwnerKind,
            record.Id,
            record.State,
            record.Version,
            record.CreatedAt,
            record.UpdatedAt,
            record.HasLiveInvokedRun
        )
        {
            InvokedRuns = runs?.Items.Select(MachineInstanceInvokedRun.From).ToList() ?? [],
            IsInvokedRunsCapped = runs?.Capped ?? false,
            QueuedInvokedRunEntryId = runs?.QueuedEntryId,
            Revision = record.Revision,
            DueAt = record.DueAt,
            Overdue = record.Overdue,
            WaitingOn = MachineInstanceWaitingRun.From(record.WaitingOn),
            InStateSince = record.InStateSince,
            Stuck = record.Stuck,
        };
}

/// <summary>
/// One train run a state-machine instance invoked: the fields the run listings show, never its
/// input or output. <c>executionDetail(id)</c> reads the rest, with sensitive members masked.
/// </summary>
/// <param name="Id">The execution's id.</param>
/// <param name="ExternalId">The execution's external id.</param>
/// <param name="Name">The train that ran (the train interface's full name).</param>
/// <param name="TrainState">Where the run is.</param>
/// <param name="StartTime">When it started (UTC).</param>
/// <param name="EndTime">When it ended (UTC), or <c>null</c> while it has not.</param>
/// <param name="FailureClass">How its failure was classified, or <c>Unclassified</c>.</param>
/// <param name="CancellationRequested">Whether a cancel has been requested for it.</param>
/// <param name="IsLive">True for the run the instance's current state waits on.</param>
public record MachineInstanceInvokedRun(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    Trax.Core.Exceptions.FailureClass FailureClass,
    bool CancellationRequested,
    bool IsLive
)
{
    internal static MachineInstanceInvokedRun From(MachineInstanceRun run) =>
        new(
            run.Id,
            run.ExternalId,
            run.TrainName,
            run.TrainState,
            run.StartTime,
            run.EndTime,
            run.FailureClass,
            run.CancellationRequested,
            run.IsLive
        );
}

/// <summary>
/// The result of <c>operations.cancelMachineInstance</c>: what the cancel did, or why it did
/// nothing, in the operations service's words, which the dashboard shows too.
/// </summary>
/// <param name="Success">True when the run was cancelled or its cancel requested.</param>
/// <param name="Outcome">What happened, or the typed reason nothing did.</param>
/// <param name="Message">The outcome in words.</param>
/// <param name="State">The state the instance moved into, when this call moved it; <c>null</c> otherwise.</param>
public record MachineInstanceCancelResponse(
    bool Success,
    MachineInstanceCancelOutcome Outcome,
    string Message,
    string? State
)
{
    internal static MachineInstanceCancelResponse From(MachineInstanceCancelResult result) =>
        new(result.Success, result.Outcome, result.Message, result.State);
}

/// <summary>How many instances of one machine are in one state, for one owner kind.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The state.</param>
/// <param name="OwnerKind">Who owns the instances counted.</param>
/// <param name="Count">How many there are.</param>
public record MachineInstanceCount(
    string Machine,
    string State,
    SnapshotOwnerKind OwnerKind,
    long Count
)
{
    /// <summary>How many of them are stuck: a timer overdue, or waiting on a run that failed or was cancelled.</summary>
    public long Stuck { get; init; }
}

/// <summary>
/// A page of one state-machine instance's history, newest first, as <c>machineInstance { transitions }</c> reads it.
/// Pass <see cref="NextCursor"/> as the next request's <c>after</c> to read older transitions.
/// </summary>
/// <param name="Items">The transitions on this page, newest first.</param>
/// <param name="NextCursor">The revision to pass as <c>after</c> for the next page, or <c>null</c> on the last page.</param>
public record MachineInstanceTransitions(
    IReadOnlyList<MachineInstanceTransition> Items,
    long? NextCursor
)
{
    internal static MachineInstanceTransitions From(MachineInstanceTransitionPage? page) =>
        page is null
            ? new([], null)
            : new(page.Items.Select(MachineInstanceTransition.From).ToList(), page.NextCursor);
}

/// <summary>
/// One transition in a state-machine instance's history: the command that moved it, from where to where, who made
/// it, when, and the run or child machine it started.
/// </summary>
/// <remarks>
/// Like the instance it carries no context and no owner key: <see cref="Actor"/> says whether the owner, host code
/// or the machine itself made it, never which user. A run it started is named by its ids, never by its token.
/// </remarks>
/// <param name="Revision">The revision the write gave the instance.</param>
/// <param name="Kind">What wrote it: a start, a command, a save, a timer or a run's outcome.</param>
/// <param name="Trigger">
/// The command that moved it (an event, a timer's event, or <c>State.done</c>, <c>State.failed</c> or
/// <c>State.cancelled</c> for a run's outcome), or <c>null</c> for a creation, a save, or an outcome that could not
/// be applied.
/// </param>
/// <param name="Actor">Who made it: the owner, host code, or the machine itself.</param>
/// <param name="FromState">The state it left, or <c>null</c> for the instance's creation.</param>
/// <param name="ToState">The state it entered.</param>
/// <param name="At">When it was written.</param>
public record MachineInstanceTransition(
    long Revision,
    SnapshotTransitionKind Kind,
    string? Trigger,
    SnapshotTransitionActor Actor,
    string? FromState,
    string ToState,
    DateTimeOffset At
)
{
    /// <summary>
    /// When the transition before it was written: <see cref="At"/> minus this is how long the instance stayed in
    /// <see cref="FromState"/> since its previous write. <c>null</c> for the oldest recorded one.
    /// </summary>
    public DateTimeOffset? PreviousAt { get; init; }

    /// <summary>The train run entering <see cref="ToState"/> queued, or <c>null</c>.</summary>
    public MachineInstanceTransitionRun? StartedRun { get; init; }

    /// <summary>The child machine instance entering <see cref="ToState"/> spawned, or <c>null</c>.</summary>
    public MachineInstanceTransitionChild? StartedChild { get; init; }

    internal static MachineInstanceTransition From(MachineInstanceTransitionRecord record) =>
        new(
            record.Revision,
            record.Kind,
            record.Trigger,
            record.Actor,
            record.FromState,
            record.ToState,
            record.At
        )
        {
            PreviousAt = record.PreviousAt,
            StartedRun = record.StartedRun is { } run
                ? new MachineInstanceTransitionRun(
                    run.ExecutionId,
                    run.TrainState,
                    run.WorkQueueEntryId
                )
                : null,
            StartedChild = record.StartedChild is { } child
                ? new MachineInstanceTransitionChild(
                    child.Machine,
                    child.OwnerKind,
                    child.Id,
                    child.RowId
                )
                : null,
        };
}

/// <summary>The train run a transition queued: which run, and where it is now. Never its token.</summary>
/// <param name="ExecutionId">The run's id, or <c>null</c> while it is queued and has no run yet.</param>
/// <param name="TrainState">
/// Where the run is: its state once dispatched, <c>PENDING</c> while its entry is queued, <c>CANCELLED</c> when the
/// entry was cancelled before it started; <c>null</c> when neither is found any more.
/// </param>
/// <param name="WorkQueueEntryId">The run's work queue entry, or <c>null</c> when none is found.</param>
public record MachineInstanceTransitionRun(
    long? ExecutionId,
    TrainState? TrainState,
    long? WorkQueueEntryId
);

/// <summary>The child machine instance a transition spawned, named as <c>machineInstance</c> takes it. Never its owner.</summary>
/// <param name="Machine">The child's machine.</param>
/// <param name="OwnerKind">Who owns the child: the parent's owner.</param>
/// <param name="Id">The child's id.</param>
/// <param name="RowId">The child's row, which a lookup of a user's draft needs.</param>
public record MachineInstanceTransitionChild(
    string Machine,
    SnapshotOwnerKind OwnerKind,
    Guid Id,
    long RowId
);
