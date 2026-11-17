namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Input of the <c>stateMachine.advanceSnapshot</c> mutation (<see cref="AdvanceSnapshot"/>): fire one trigger on the stored draft, server-side. The server never trusts a client-computed state on this path.
/// </summary>
public record AdvanceSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>UNKNOWN_MACHINE</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>The trigger to fire (a machine trigger name, e.g. "Next").</summary>
    public required string Trigger { get; init; }

    /// <summary>
    /// Optional trigger input as JSON, at most <see cref="SnapshotLimits.MaxSnapshotBytes"/> bytes (UTF-8);
    /// a larger one is refused as <c>TOO_LARGE</c> before it is parsed.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Optional fields of the trigger input that are recorded masked, as a JSON object: every copy of this run's input
    /// Trax keeps writes it as <c>{"_redacted": true}</c>. Its fields are added to <see cref="Input"/>'s object before
    /// the trigger fires, so a field must not be in both, and <see cref="Input"/> must then be an object or absent.
    /// A machine's typed surface puts the fields its input record marks <c>[TraxSensitive]</c> here. Counted with
    /// <see cref="Input"/> against <see cref="SnapshotLimits.MaxSnapshotBytes"/>.
    /// </summary>
    [Trax.Effect.Attributes.TraxSensitive]
    public string? SensitiveInput { get; init; }

    /// <summary>
    /// Optional idempotency key so a retry replays instead of re-firing. A retry must repeat the trigger:
    /// the same key sent with a different trigger is refused as <c>REQUEST_ID_REUSED</c>. Advance and send
    /// share one key space. A blank key, or one holding a control character, is refused as
    /// <c>INVALID_REQUEST_ID</c>.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch with the
    /// server's is refused with a <c>SCHEMA_MISMATCH</c> problem before the advance runs. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }

    /// <summary>
    /// Optional: the snapshot the client's twin computed for this advance, as canonical JSON. When present the
    /// server compares it against its own authoritative result before persisting anything; a divergence is
    /// refused with a <c>CLIENT_DIVERGENCE</c> problem and the stored draft is left unchanged, catching a
    /// client/server engine mismatch on real input. Absent = no check. At most
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/> bytes (UTF-8): a larger one is refused as <c>TOO_LARGE</c> before
    /// it is compared. It is recorded masked, and a divergence logs only its length and SHA-256 hash.
    /// </summary>
    [Trax.Effect.Attributes.TraxSensitive]
    public string? ClientResult { get; init; }
}
