namespace Trax.Effect.StateMachine.Persistence.Mutations;

// ONE set of contracts for ALL machines. Every input carries a `machine` discriminator the resolver looks
// up in the registry, so there is no per-machine mutation fan and no per-machine CLR types. The context
// crosses the wire as opaque canonical JSON, so a single input/output shape serves every machine.

/// <summary>
/// Input of the <c>stateMachine.saveSnapshot</c> mutation (<see cref="SaveSnapshot"/>): an autosave of a
/// client-computed snapshot for any registered machine. One shape serves every machine; the context
/// travels as opaque canonical JSON.
/// </summary>
public record SaveSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout").</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user.</summary>
    public required Guid Id { get; init; }

    /// <summary>The whole client-computed snapshot as canonical JSON. The server validates it before storing.</summary>
    public required string Snapshot { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>, embedded in the twin).
    /// When present and it differs from the server's, the request is refused with a <c>schema-mismatch</c>
    /// problem so a stale client reloads instead of writing under an outdated contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.saveSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set: refusals come back as data, never as a GraphQL error.
/// </summary>
public record SaveSnapshotOutput
{
    /// <summary>The snapshot as stored, in the machine's canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Why the save was refused (for example <c>unauthenticated</c>, <c>unknown-machine</c>, <c>schema-mismatch</c>, <c>too-large</c>, <c>draft-committed</c>, <c>conflict</c> or a rehydration error code); null on success. Nothing was written when it is set.</summary>
    public SnapshotProblem? Problem { get; init; }
}

/// <summary>
/// Input of the <c>stateMachine.advanceSnapshot</c> mutation (<see cref="AdvanceSnapshot"/>): fire one trigger on the stored draft, server-side. The server never trusts a client-computed state on this path.
/// </summary>
public record AdvanceSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>unknown-machine</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>The trigger to fire (a machine trigger name, e.g. "Next").</summary>
    public required string Trigger { get; init; }

    /// <summary>
    /// Optional trigger input as JSON, at most <see cref="SnapshotLimits.MaxSnapshotBytes"/> bytes (UTF-8);
    /// a larger one is refused as <c>too-large</c> before it is parsed.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Optional idempotency key so a retry replays instead of re-firing. A retry must repeat the trigger:
    /// the same key sent with a different trigger is refused as <c>request-id-reused</c>. Advance and send
    /// share one key space.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch with the
    /// server's is refused with a <c>schema-mismatch</c> problem before the advance runs. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }

    /// <summary>
    /// Optional: the snapshot the client's twin computed for this advance, as canonical JSON. When present the
    /// server compares it against its own authoritative result before persisting anything; a divergence is
    /// refused with a <c>client-divergence</c> problem and the stored draft is left unchanged, catching a
    /// client/server engine mismatch on real input. Absent = no check.
    /// </summary>
    public string? ClientResult { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.advanceSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set: refusals come back as data, never as a GraphQL error.
/// </summary>
public record AdvanceSnapshotOutput
{
    /// <summary>The draft after the advance (or the current draft, when the request replayed), in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Why the advance was refused (for example <c>not-found</c>, <c>conflict</c>, <c>request-id-reused</c>, <c>client-divergence</c>, <c>schema-mismatch</c> or a machine rejection reason); null on success. The stored draft is unchanged when it is set.</summary>
    public SnapshotProblem? Problem { get; init; }
}

/// <summary>
/// Input of the <c>stateMachine.loadSnapshot</c> mutation (<see cref="LoadSnapshot"/>): resume the caller's stored draft.
/// </summary>
public record LoadSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>unknown-machine</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>schema-mismatch</c> problem so a stale client reloads rather than rehydrating a draft shaped
    /// by a newer contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.loadSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set.
/// </summary>
public record LoadSnapshotOutput
{
    /// <summary>The stored draft in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Set when there is no such draft (normal: start fresh) or the stored data failed validation.</summary>
    public SnapshotProblem? Problem { get; init; }
}

/// <summary>
/// Input of the <c>stateMachine.sendSnapshot</c> mutation (<see cref="SendSnapshot"/>): run the machine's one irreversible effect exactly once and advance the draft with its receipt.
/// </summary>
public record SendSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>unknown-machine</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The draft id, scoped to the authenticated user: two users' drafts never collide on it.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Idempotency key. A stable value per intended send; if absent, <c>send:{Id}</c> is used. It shares
    /// one key space with <see cref="AdvanceSnapshotInput.RequestId"/>, so a key an advance already used is
    /// refused as <c>request-id-reused</c>, before the effect runs.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>schema-mismatch</c> problem so a stale client cannot trigger the irreversible send under an
    /// outdated contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.sendSnapshot</c> mutation. Exactly one of <see cref="Snapshot"/> and
/// <see cref="Problem"/> is set: refusals come back as data, never as a GraphQL error.
/// </summary>
public record SendSnapshotOutput
{
    /// <summary>The draft after the effect ran and the machine advanced (or the replayed result of an earlier send), in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>Why the send was refused or failed (for example <c>no-effect</c>, <c>not-found</c>, <c>request-id-reused</c>, or <c>delivery-failed</c> when the effect threw and the draft was not advanced, so the send can be retried); null on success.</summary>
    public SnapshotProblem? Problem { get; init; }
}
