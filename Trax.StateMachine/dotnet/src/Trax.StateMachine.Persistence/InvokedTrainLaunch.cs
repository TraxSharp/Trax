using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

/// <summary>One run an invoking state queues.</summary>
/// <param name="TrainType">The train's interface, its canonical name.</param>
/// <param name="Input">The run's input, built from the context the state was entered with.</param>
/// <param name="ExternalId">The id the run's work queue entry takes, which the instance stores as its invoke token.</param>
/// <param name="InvokedBy">The instance invoking it, written on the entry and carried to the run.</param>
/// <param name="State">The invoking state being entered, named in a refusal.</param>
internal sealed record InvokedTrainLaunch(
    Type TrainType,
    object Input,
    string ExternalId,
    InvokedBy InvokedBy,
    string State
)
{
    /// <summary>
    /// True when the state was entered by the outcome of the run before it rather than by a caller: an
    /// <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c> target that itself invokes a train. No user is present
    /// to authorize against, so only a system-owned instance may launch one, in Trax's trusted execution scope.
    /// The startup check refuses a user-owned machine whose outcome enters an invoking state, and the launcher
    /// refuses such a launch for a user-owned instance with <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public bool FromOutcome { get; init; }

    /// <summary>
    /// The owner of the user-owned instance whose state queues the run, as a run starter (<c>RunStarter</c>), so the
    /// owner may follow it and the invoked-run cap counts it; null for a system-owned instance.
    /// </summary>
    public string? StarterHash { get; init; }
}
