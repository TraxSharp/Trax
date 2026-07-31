namespace Trax.Effect.StateMachine.Persistence;

/// <summary>Guard rails for the persistence layer.</summary>
public static class SnapshotLimits
{
    /// <summary>
    /// The largest client-provided snapshot the autosave path will accept, checked BEFORE any parse or
    /// DB work (a DoS guard). Generous for a form-style flow; a hostile megabyte payload is rejected
    /// as <c>too-large</c>.
    /// </summary>
    public const int MaxSnapshotBytes = 64 * 1024;

    /// <summary>
    /// The default lease for an exactly-once effect claim. Long enough that a real effect completes
    /// within it; if a runner dies mid-effect, the next caller reclaims the key after the lease passes.
    /// </summary>
    public static readonly TimeSpan DefaultEffectLease = TimeSpan.FromMinutes(5);
}

/// <summary>A typed problem returned as DATA from a mutation (never a thrown error across the API boundary).</summary>
public sealed record SnapshotProblem
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
