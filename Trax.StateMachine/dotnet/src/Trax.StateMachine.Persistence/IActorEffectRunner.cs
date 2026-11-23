using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// An effect runner that checks the effect-bound event's rule against the actor before it claims the effect.
/// </summary>
internal interface IActorEffectRunner
{
    /// <summary>
    /// <see cref="ISnapshotEffectRunner.Run"/> on behalf of <paramref name="actor"/>: a client the effect-bound
    /// event's rule does not admit, or any actor that is not a client, is refused as <c>FORBIDDEN</c> before the
    /// draft is read or the effect claimed.
    /// </summary>
    Task<AdvanceOutcome> Run(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Finishes the effect pending on <paramref name="userKey"/>'s draft <paramref name="id"/> once its claimant's lease
    /// has passed: takes the mark over, runs the effect again under the same key, and records it as the answer to the
    /// request that sent it. False when nothing was finished (no such mark, its lease is live, another host took it).
    /// The send was authorized when it was made; nothing is checked against a caller here.
    /// </summary>
    Task<bool> FinishAbandoned(string userKey, Guid id, CancellationToken cancellationToken);
}
