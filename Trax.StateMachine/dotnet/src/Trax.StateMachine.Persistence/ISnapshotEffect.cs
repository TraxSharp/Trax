using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The single irreversible side effect bound to a consequential transition (send a letter, charge a
/// card, provision a resource). It returns a receipt (a downstream id) recorded in the snapshot. Run
/// through <see cref="IdempotentEffect"/> so it fires exactly once per intent. The receipt must be non-empty:
/// a null or empty one is treated as a failed delivery, and the send reports <c>DELIVERY_FAILED</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/i-snapshot-effect">SDK reference</seealso>
public interface ISnapshotEffect
{
    /// <summary>
    /// Performs the side effect for <paramref name="snapshot"/> (the draft as loaded, in the effect's from-state)
    /// and returns its receipt, which is written into the terminal snapshot's context. Throw if the effect did not
    /// happen: the draft is left as it was and a retry runs it again. An <see cref="OperationCanceledException"/> (an
    /// outbound call's timeout included) means the outcome is unknown instead: the effect stays pending on the draft
    /// until its lease passes, and is then run again under the same key, by the next send or by the reconciler.
    /// </summary>
    /// <param name="snapshot">The draft the effect acts on.</param>
    /// <param name="cancellationToken">
    /// The effect runner passes <see cref="CancellationToken.None"/>: a request that goes away once the effect has
    /// started does not stop it. Bound a slow downstream call with its own timeout, never after the irreversible
    /// step.
    /// </param>
    /// <returns>A non-empty receipt, typically the downstream system's id for what was done.</returns>
    Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the side effect as <see cref="Run(Snapshot, CancellationToken)"/> does, given the effect's key: minted
    /// when a send marks the draft, and the same for every run of that send, a run again after its claimant stopped
    /// without recording a result included; the next send of the event, after a reset for instance, has a new one. Pass
    /// it to the system the effect calls as its idempotency key, so a repeat is recognised there. The default ignores
    /// the key and calls <see cref="Run(Snapshot, CancellationToken)"/>.
    /// </summary>
    /// <param name="snapshot">The draft the effect acts on.</param>
    /// <param name="effectKey">The effect's key, stable across every run of one intent.</param>
    /// <param name="cancellationToken">As for <see cref="Run(Snapshot, CancellationToken)"/>.</param>
    /// <returns>A non-empty receipt, typically the downstream system's id for what was done.</returns>
    Task<string> Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken) =>
        Run(snapshot, cancellationToken);
}
