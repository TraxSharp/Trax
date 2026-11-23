using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The advance a draft records so a retry can be recognised: the client's idempotency key, the trigger it
/// fired, and the state it fired from. <see cref="Trigger"/> and <see cref="FromState"/> are <see langword="null"/>
/// only on a row written before they were recorded, and such a request is never replayed.
/// </summary>
public sealed record AppliedRequest(string RequestId, string? Trigger, string? FromState)
{
    /// <summary>
    /// The row's ring of recent client requests with this request recorded as the newest, which the store writes
    /// with it; null leaves the stored ring as it is.
    /// </summary>
    internal IReadOnlyList<RecordedRequest>? Ring { get; init; }
}
