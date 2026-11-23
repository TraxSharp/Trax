using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The effect pending on a row: its claimant, the key it runs under, its event, the request that sent it, and when the
/// lease ends.
/// </summary>
/// <param name="Token">The claimant's token, which guards the writes that finish or end the effect.</param>
/// <param name="Key">The key the effect runs under, the same for every run of it.</param>
/// <param name="Trigger">The effect-bound event.</param>
/// <param name="RequestId">The client request that sent it, or null when none was recorded.</param>
/// <param name="LeaseUntil">When the claimant's lease ends, on the database's clock.</param>
internal sealed record PendingEffect(
    Guid Token,
    string? Key,
    string? Trigger,
    string? RequestId,
    DateTimeOffset? LeaseUntil
);
