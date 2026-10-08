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
        );
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

    /// <summary>
    /// The work queue entry of the run the state waits on while it is still queued, and so not
    /// yet in <see cref="InvokedRuns"/>; <c>null</c> otherwise.
    /// </summary>
    public long? QueuedInvokedRunEntryId { get; init; }

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
);
