using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// The result of <c>operations.cancelMachineInstance</c>: what the cancel did, or why it did
/// nothing, in the operations service's words, which the dashboard shows too.
/// </summary>
/// <param name="Success">True when the run was cancelled or its cancel requested.</param>
/// <param name="Outcome">What happened, or the typed reason nothing did.</param>
/// <param name="Message">The outcome in words.</param>
/// <param name="State">The state the instance moved into, when this call moved it; <see langword="null"/> otherwise.</param>
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
