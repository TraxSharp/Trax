using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>The result of an operator's cancel of a state-machine instance.</summary>
/// <param name="Outcome">What happened, or why nothing did.</param>
/// <param name="Message">
/// The outcome in words. The dashboard and the API show this same text (central <c>docs/0022</c>).
/// </param>
/// <param name="State">
/// The state the instance is in after <see cref="MachineInstanceCancelOutcome.Moved"/>; null otherwise.
/// </param>
public record MachineInstanceCancelResult(
    MachineInstanceCancelOutcome Outcome,
    string Message,
    string? State = null
)
{
    /// <summary>True when the run was cancelled or its cancel requested.</summary>
    public bool Success =>
        Outcome
            is MachineInstanceCancelOutcome.Moved
                or MachineInstanceCancelOutcome.RunCancelled
                or MachineInstanceCancelOutcome.CancelRequested;
}
