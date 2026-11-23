using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The machine-agnostic face of <see cref="SnapshotEffectRunner{TState,TTrigger}"/> (for the registry).</summary>
public interface ISnapshotEffectRunner
{
    /// <summary>
    /// Runs the machine's effect for the caller's draft at most once and commits its receipt by firing the
    /// effect's trigger. A <paramref name="requestId"/> the draft recorded for this send replays its recorded result,
    /// wherever the draft is now, and one recorded for a different trigger is <see cref="AdvanceOutcome.Rejected"/>.
    /// Otherwise a draft already in the target state replays as <see cref="AdvanceOutcome.Advanced"/> without running
    /// anything, and a draft in any state other than the effect's from-state is <see cref="AdvanceOutcome.Rejected"/>
    /// before the effect runs; an effect already pending on the draft is rejected as <c>EFFECT_IN_PROGRESS</c> until its
    /// lease passes, when this send takes it over and runs it again under the same key. Over the Trax store the effect
    /// is pending on the draft's row while it runs, nothing else writes the draft, and its receipt, the move and the end
    /// of the mark are one write. Over a store a host substituted, the claim ledger keeps the effect, and a claim whose
    /// effect already ran on other content is rejected as <c>DRAFT_CHANGED</c>.
    /// </summary>
    /// <param name="userKey">The authenticated owner of the draft; a null, empty or whitespace key is refused.</param>
    /// <param name="id">The draft id.</param>
    /// <param name="requestId">The client's idempotency key for this send; a retry with the same id replays.</param>
    /// <param name="cancellationToken">
    /// Cancels the request up to the effect. It is not passed to the effect, which gets
    /// <see cref="CancellationToken.None"/>, and once the effect has returned, its receipt and the advance that
    /// records it are written whatever the token says, so a cancelled request never leaves an effect that ran
    /// looking as if it had not.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="userKey"/> is null, empty or whitespace.</exception>
    Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    );
}
