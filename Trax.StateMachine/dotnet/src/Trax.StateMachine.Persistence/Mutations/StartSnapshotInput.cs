namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Input of the <c>stateMachine.startSnapshot</c> mutation (<see cref="StartSnapshot"/>): create a new instance of
/// a machine that declares a <c>start</c>, for the caller. The server mints the id and applies the start's declared
/// reduction to the initial context; the client never writes a snapshot.
/// </summary>
public record StartSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>UNKNOWN_MACHINE</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>
    /// The start input as a JSON object, validated against the start's declared record (never the context type).
    /// Omit it when the start's record has no required field. At most <see cref="SnapshotLimits.MaxSnapshotBytes"/>
    /// bytes (UTF-8); a larger one is refused as <c>TOO_LARGE</c> before it is parsed, and one that is not a JSON
    /// object as <c>MALFORMED</c>.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Optional fields of the start input that are recorded masked, as a JSON object, merged into
    /// <see cref="Input"/> before the start runs, as <see cref="AdvanceSnapshotInput.SensitiveInput"/> is.
    /// </summary>
    [Trax.Effect.Attributes.TraxSensitive]
    public string? SensitiveInput { get; init; }

    /// <summary>
    /// The idempotency key of this start, required: a retry with the same key and input returns the instance the
    /// first request created, and the same key with a different input is refused as <c>REQUEST_ID_REUSED</c>.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused with a
    /// <c>SCHEMA_MISMATCH</c> problem before anything is written. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}
