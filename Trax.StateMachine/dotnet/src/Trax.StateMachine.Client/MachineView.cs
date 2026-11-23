using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What a controller holds, as one immutable value: a new one on every change.</summary>
public sealed record MachineView
{
    /// <summary>Loading until the first snapshot from the server, stopped once gone or outdated.</summary>
    public MachineStatus Status { get; init; }

    /// <summary>The instance's id, once started or given.</summary>
    public string? Id { get; init; }

    /// <summary>What to show: the newest prediction while an event is pending, else what the server confirmed.</summary>
    public Snapshot? Snapshot { get; init; }

    /// <summary>The last snapshot the server confirmed.</summary>
    public Snapshot? Confirmed { get; init; }

    /// <summary>The incarnation of the instance the view holds.</summary>
    public string? RowId { get; init; }

    /// <summary>The revision of <see cref="Confirmed"/>.</summary>
    public long? Revision { get; init; }

    /// <summary>When the state's timer is due, as the server says; null when it has none. The client never fires it.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>Whether an event is in flight or queued.</summary>
    public bool Pending { get; init; }

    /// <summary>The last refusal or transport failure, until the next event the server confirms.</summary>
    public MachineProblem? Problem { get; init; }

    /// <summary>Why the controller stopped, once it has.</summary>
    public MachineError? Error { get; init; }

    /// <summary>
    /// Whether a sync (<see cref="ServerMachineController.SyncToAsync"/>) is running, or the last one did not settle
    /// <see cref="SyncOutcome.Synced"/>: the server is not where the page is. A page holds a leave guard on it.
    /// </summary>
    public bool Unsaved { get; init; }
}
