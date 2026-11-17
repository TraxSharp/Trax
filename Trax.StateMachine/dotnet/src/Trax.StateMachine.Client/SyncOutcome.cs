using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What one sync came to. Never an exception.</summary>
public abstract record SyncOutcome
{
    private SyncOutcome() { }

    /// <summary>The server is at the target with the fields applied, as <see cref="Snapshot"/> holds it.</summary>
    public sealed record Synced(ServerSnapshot Snapshot) : SyncOutcome;

    /// <summary>A later sync took over; this one sent nothing after the event it had in flight.</summary>
    public sealed record Superseded : SyncOutcome;

    /// <summary>No events reach the target from what the server holds.</summary>
    public sealed record Unreachable : SyncOutcome;

    /// <summary>The planner stopped at its depth, or the sync sent as many events as it may (twice the machine's states).</summary>
    public sealed record Exceeded : SyncOutcome;

    /// <summary>
    /// The target is not a state, <see cref="Trigger"/> is not one a client may move with, or <see cref="Code"/> says
    /// the fields input or the confirmed context nests too deep (<see cref="PlanResult.Invalid"/>).
    /// </summary>
    public sealed record Invalid(string? Trigger, string? Target) : SyncOutcome
    {
        /// <summary><c>INVALID_INPUT</c> or <c>INVALID_CONTEXT</c> for a value nested too deep to plan with, or null.</summary>
        public RefusalCode? Code { get; init; }
    }

    /// <summary>The server refused an event twice, the second time after the instance was read again.</summary>
    public sealed record Refused(MachineProblem Problem) : SyncOutcome;

    /// <summary>An event did not get through; syncing again plans from what the server confirmed.</summary>
    public sealed record Failed(MachineProblem Problem) : SyncOutcome;

    /// <summary>The controller stopped for good before or while it synced.</summary>
    public sealed record Stopped(MachineError Error) : SyncOutcome;
}
