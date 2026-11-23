namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Input of the <c>stateMachine.sendSnapshot</c> mutation (<see cref="SendSnapshot"/>): run the machine's one irreversible effect exactly once and advance the draft with its receipt.
/// </summary>
public record SendSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>UNKNOWN_MACHINE</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Idempotency key. A stable value per intended send; if absent, <c>send:{Id}</c> is used. It shares
    /// one key space with <see cref="AdvanceSnapshotInput.RequestId"/>, so a key an advance already used is
    /// refused as <c>REQUEST_ID_REUSED</c>, before the effect runs. A blank key, or one holding a control
    /// character, is refused as <c>INVALID_REQUEST_ID</c>, also before the effect runs.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>SCHEMA_MISMATCH</c> problem so a stale client cannot trigger the irreversible send under an
    /// outdated contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}
