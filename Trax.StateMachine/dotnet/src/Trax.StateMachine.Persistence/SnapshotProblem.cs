namespace Trax.StateMachine.Persistence;

/// <summary>A typed problem returned as DATA from a mutation (never a thrown error across the API boundary).</summary>
public sealed record SnapshotProblem
{
    /// <summary>The code a client branches on, such as <see cref="RefusalCode.UnknownMachine"/> or <see cref="RefusalCode.NoTransition"/>.</summary>
    public required RefusalCode Code { get; init; }

    /// <summary>A human-readable explanation for logs or display. Its wording is not a contract; branch on <see cref="Code"/>.</summary>
    public required string Message { get; init; }
}
