using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// Every code a machine operation is refused with, by the engine, the draft service, the effect runner or the API. It
/// is one closed set: the engines' corpora, the GraphQL <c>RefusalCode</c> enum and the TypeScript package's
/// <c>RefusalCode</c> all hold these members, and each travels as its <see cref="RefusalCodes.ToWire"/> spelling,
/// such as <c>GUARD_FAILED</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/result-codes">SDK reference</seealso>
[JsonConverter(typeof(RefusalCodeJsonConverter))]
public enum RefusalCode
{
    /// <summary>No transition is wired from the current state on the given trigger (or the state/trigger token is unknown).</summary>
    NoTransition,

    /// <summary>A transition exists but its guard rejected the supplied input.</summary>
    GuardFailed,

    /// <summary>
    /// A context fails its state's validator: produced by a transition (a reducer bug), or found in a stored or
    /// submitted snapshot.
    /// </summary>
    InvalidContext,

    /// <summary>An unexpected error was caught inside the engine. Surfaced as a refusal so the caller never sees an exception.</summary>
    InternalError,

    /// <summary>The trigger input does not match the trigger's declared input schema.</summary>
    InvalidInput,

    /// <summary>The trigger is an invoked train's outcome, which only the outcome path applies, never an advance.</summary>
    OutcomeBound,

    /// <summary>The trigger is a state's timer, which only the server fires, never an advance.</summary>
    TimerBound,

    /// <summary>The trigger input or the resulting snapshot is past the snapshot size cap.</summary>
    TooLarge,

    /// <summary>
    /// A refusal of <c>discard</c>: the instance is in a state only the server puts it in (a committed state, an
    /// effect's target, a state that invokes a train, a state an invoked train's outcome reaches, or the target of an
    /// event gated by a role or a policy), or an effect on it is running or recording its result.
    /// </summary>
    DiscardNotAllowed,

    /// <summary>
    /// An effect is pending on the instance: every other write to it waits until the effect is recorded or has
    /// failed. Retry once it has finished.
    /// </summary>
    EffectInProgress,

    /// <summary>Stored or submitted JSON was not valid JSON, was not an object, or was missing a required field.</summary>
    Malformed,

    /// <summary>The snapshot or the request names a machine this host does not serve.</summary>
    UnknownMachine,

    /// <summary>The snapshot was produced against a different definition version.</summary>
    VersionMismatch,

    /// <summary>The snapshot's state token is not a state of this machine.</summary>
    UnknownState,

    /// <summary>The caller does not meet the rule the event, start or read declares.</summary>
    Forbidden,

    /// <summary>The request id was already used for a different request.</summary>
    RequestIdReused,

    /// <summary>The trigger runs the machine's irreversible effect from this state, so only a send fires it, never an advance.</summary>
    EffectBound,

    /// <summary>A save is not allowed for this machine, because its states are reached through declared events only.</summary>
    SaveNotAllowed,

    /// <summary>The client runs an older version of the machine than the server, and must reload.</summary>
    ClientOutdated,

    /// <summary>The client runs a newer version of the machine than this server; a retry may reach a newer one.</summary>
    ServerOutdated,

    /// <summary>The user already holds as many drafts as one user may, so a start or a save creating another is refused.</summary>
    TooManyDrafts,

    /// <summary>The request id is missing, empty, whitespace, too long, or holds a character a row cannot store.</summary>
    InvalidRequestId,

    /// <summary>The caller names no user, so there is no owner to act for.</summary>
    Unauthenticated,

    /// <summary>The machine declares no <c>start</c>, so a client cannot create an instance of it.</summary>
    NoStart,

    /// <summary>The instance belongs to the system: only host code creates, drives or deletes it.</summary>
    SystemOwned,

    /// <summary>
    /// The caller has no instance with this id that it may read: a missing id, another user's, a system instance's,
    /// and one the read rule refuses the caller all get this one answer, so it tells the caller nothing about which.
    /// </summary>
    NotFound,

    /// <summary>The caller already holds as many open subscriptions as one principal may; one that completes frees its place.</summary>
    TooManySubscriptions,

    /// <summary>The client sent a twin's IR hash that differs from the machine this host serves; it must reload.</summary>
    SchemaMismatch,

    /// <summary>The draft changed elsewhere while this write ran; reload and retry.</summary>
    Conflict,

    /// <summary>The machine declares no irreversible effect, so there is nothing to send.</summary>
    NoEffect,

    /// <summary>The effect, or the store around it, failed: the draft was not advanced, so the caller can retry.</summary>
    DeliveryFailed,

    /// <summary>The snapshot a client computed differs from the one the server computed for the same transition.</summary>
    ClientDivergence,

    /// <summary>The state is completed by its action, not by a save: a client sends the action instead.</summary>
    StateReserved,

    /// <summary>The instance is in a state that invokes a train: it moves on when the train finishes, or through one of its actions, never through a save.</summary>
    DraftInvoking,

    /// <summary>The stored draft cannot be read by this machine definition, so only a start-over overwrites it.</summary>
    DraftUnreadable,

    /// <summary>The draft was already completed, so an edit may not overwrite it.</summary>
    DraftCommitted,

    /// <summary>The draft's action already ran on content that has changed since; its result is recorded only once that content is restored.</summary>
    DraftChanged,

    /// <summary>The owner already has as many invoked runs live as one owner may, so the step that starts another is refused.</summary>
    InvokeLimitReached,

    /// <summary>The user entering the state may not run the train it invokes.</summary>
    InvokeForbidden,

    /// <summary>
    /// An outcome or a timer would enter a state that invokes a train on a user-owned instance: only a system-owned
    /// instance chains runs that way.
    /// </summary>
    InvokeChainUserOwned,
}
