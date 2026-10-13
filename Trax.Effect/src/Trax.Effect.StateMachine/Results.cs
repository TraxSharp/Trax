namespace Trax.Effect.StateMachine;

/// <summary>
/// The reasons <see cref="SnapshotMachine{TState,TTrigger}.Advance"/> can decline to move.
/// These strings are part of the cross-language contract: the shared conformance fixtures assert
/// on them, so the TypeScript reducer must use the identical values.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/result-codes">SDK reference</seealso>
public static class RejectionReasons
{
    /// <summary>No transition is wired from the current state on the given trigger (or the state/trigger token is unknown).</summary>
    public const string NoTransition = "no-transition";

    /// <summary>A transition exists but its guard rejected the supplied input.</summary>
    public const string GuardFailed = "guard-failed";

    /// <summary>The transition ran but produced a context that fails the target state's validator (a reducer bug).</summary>
    public const string InvalidContext = "invalid-context";

    /// <summary>An unexpected error was caught inside the engine. Surfaced as a rejection so the caller never sees an exception.</summary>
    public const string InternalError = "internal-error";

    /// <summary>The trigger input does not match the trigger's declared input schema.</summary>
    public const string InvalidInput = "invalid-input";

    /// <summary>The trigger is an invoked train's outcome, which only the outcome path applies, never an advance.</summary>
    public const string OutcomeBound = "outcome-bound";

    /// <summary>The trigger is a state's timer, which only the server fires, never an advance.</summary>
    public const string TimerBound = "timer-bound";

    /// <summary>The trigger input or the resulting snapshot is past the snapshot size cap.</summary>
    public const string TooLarge = "too-large";

    /// <summary>
    /// A refusal of <c>discard</c>, not of an advance: the server put the instance in its state, it is in a state that
    /// invokes a train, or an effect is pending on it, so its owner may not discard it. Only the draft service answers
    /// it.
    /// </summary>
    public const string DiscardNotAllowed = "discard-not-allowed";

    /// <summary>
    /// A server refusal: an effect is pending on the instance, so every other write to it waits until the effect is
    /// recorded or fails. Retry once it has finished. Only the server answers it.
    /// </summary>
    public const string EffectInProgress = "effect-in-progress";
}

/// <summary>
/// The reasons <see cref="SnapshotMachine{TState,TTrigger}.Rehydrate"/> can reject stored JSON.
/// Part of the cross-language contract (see <see cref="RejectionReasons"/>).
/// </summary>
public static class RehydrationErrorCodes
{
    /// <summary>The input was not valid JSON, was not an object, or was missing a required field.</summary>
    public const string Malformed = "malformed";

    /// <summary>The snapshot names a different machine than this definition.</summary>
    public const string UnknownMachine = "unknown-machine";

    /// <summary>The snapshot was produced against a different definition version.</summary>
    public const string VersionMismatch = "version-mismatch";

    /// <summary>The snapshot's state token is not a state of this machine.</summary>
    public const string UnknownState = "unknown-state";

    /// <summary>The context does not satisfy the state's validator.</summary>
    public const string InvalidContext = "invalid-context";
}

/// <summary>
/// The total result of an <see cref="SnapshotMachine{TState,TTrigger}.Advance"/>. Exactly one of
/// <see cref="Transitioned"/> or <see cref="Rejected"/> — never an exception.
/// </summary>
public abstract record AdvanceResult
{
    /// <summary>The trigger was accepted; <see cref="Snapshot"/> is the validated successor.</summary>
    public sealed record Transitioned(Snapshot Snapshot) : AdvanceResult;

    /// <summary>The trigger was declined; <see cref="Reason"/> is one of <see cref="RejectionReasons"/>.</summary>
    public sealed record Rejected(string Reason, string? Detail = null) : AdvanceResult
    {
        /// <summary>The exception behind an <c>internal-error</c>, kept for the server's log and never in <see cref="Detail"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    // Private ctor seals the hierarchy to the two nested cases above.
    private AdvanceResult() { }
}

/// <summary>
/// The total result of a <see cref="SnapshotMachine{TState,TTrigger}.Rehydrate"/>. Exactly one of
/// <see cref="Ok"/> or <see cref="Error"/> — never an exception. This is the "parse, don't validate"
/// boundary: raw jsonb from a database becomes either a typed <see cref="Snapshot"/> or a typed error.
/// </summary>
public abstract record RehydrationResult
{
    /// <summary>The JSON was a well-formed snapshot for this definition.</summary>
    public sealed record Ok(Snapshot Snapshot) : RehydrationResult;

    /// <summary>The JSON could not be accepted; <see cref="Code"/> is one of <see cref="RehydrationErrorCodes"/>.</summary>
    public sealed record Error(string Code, string Message) : RehydrationResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    private RehydrationResult() { }
}
