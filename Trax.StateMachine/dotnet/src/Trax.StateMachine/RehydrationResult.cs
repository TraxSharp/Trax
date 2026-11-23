namespace Trax.StateMachine;

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
