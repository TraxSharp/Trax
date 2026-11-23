namespace Trax.StateMachine.Persistence.Mutations;

/// <summary>
/// Input of the <c>stateMachine.loadSnapshot</c> mutation (<see cref="LoadSnapshot"/>): resume the caller's stored draft.
/// </summary>
public record LoadSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>UNKNOWN_MACHINE</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>SCHEMA_MISMATCH</c> problem so a stale client reloads rather than rehydrating a draft shaped
    /// by a newer contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}
