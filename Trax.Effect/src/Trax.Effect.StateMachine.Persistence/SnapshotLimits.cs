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
    /// Whether a trigger's input is past <see cref="MaxSnapshotBytes"/> as JSON, checked on every path that fires
    /// a trigger before the input is validated or the draft is loaded, so an oversized input is <c>too-large</c>
    /// and costs no database work. An input that cannot be written as JSON is left to validation, which refuses
    /// it as <c>invalid-input</c>.
    /// </summary>
    internal static bool InputTooLarge(System.Text.Json.Nodes.JsonNode? input)
    {
        if (input is null)
            return false;
        try
        {
            return System.Text.Encoding.UTF8.GetByteCount(input.ToJsonString()) > MaxSnapshotBytes;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The default lease for an exactly-once effect claim. Long enough that a real effect completes
    /// within it; if a runner dies mid-effect, the next caller reclaims the key after the lease passes.
    /// </summary>
    public static readonly TimeSpan DefaultEffectLease = TimeSpan.FromMinutes(5);
}

/// <summary>A typed problem returned as DATA from a mutation (never a thrown error across the API boundary).</summary>
public sealed record SnapshotProblem
{
    /// <summary>
    /// A stable kebab-case code a client can branch on, such as <c>unknown-machine</c>, <c>malformed</c>,
    /// <c>too-large</c>, <c>conflict</c> or <c>no-transition</c>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>A human-readable explanation for logs or display. Its wording is not a contract; branch on <see cref="Code"/>.</summary>
    public required string Message { get; init; }
}
