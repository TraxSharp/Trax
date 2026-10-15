namespace Trax.Effect.StateMachine;

/// <summary>
/// The total result of an <see cref="SnapshotMachine{TState,TTrigger}.Advance"/>. Exactly one of
/// <see cref="Transitioned"/> or <see cref="Rejected"/> — never an exception.
/// </summary>
public abstract record AdvanceResult
{
    /// <summary>The trigger was accepted; <see cref="Snapshot"/> is the validated successor.</summary>
    public sealed record Transitioned(Snapshot Snapshot) : AdvanceResult;

    /// <summary>The trigger was declined; <see cref="Reason"/> says why.</summary>
    public sealed record Rejected(RefusalCode Reason, string? Detail = null) : AdvanceResult
    {
        /// <summary>The exception behind an <c>INTERNAL_ERROR</c>, kept for the server's log and never in <see cref="Detail"/>.</summary>
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

    /// <summary>The JSON could not be accepted; <see cref="Code"/> says why.</summary>
    public sealed record Error(RefusalCode Code, string Message) : RehydrationResult
    {
        /// <summary>The exception behind an unexpected failure, kept for the server's log and never in <see cref="Message"/>.</summary>
        internal Exception? Exception { get; init; }
    }

    private RehydrationResult() { }
}
