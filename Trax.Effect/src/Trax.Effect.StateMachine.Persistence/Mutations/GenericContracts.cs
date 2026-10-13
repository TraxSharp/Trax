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

    /// <summary>
    /// The whole client-computed snapshot as canonical JSON. The server validates it before storing. It carries the
    /// instance's context, so every copy of this run's input Trax keeps records it masked.
    /// </summary>
    [Trax.Effect.Attributes.TraxSensitive]
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

    /// <summary>
    /// The instance's revision after this request, bumped by the database on every write, or <c>null</c> where the
    /// store does not record one. It travels beside <see cref="Snapshot"/>, never inside it, and orders the writes
    /// of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one. <c>null</c> where the store does not record one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <c>null</c> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
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
    /// the same key sent with a different trigger is refused as <c>request-id-reused</c>. Advance and send
    /// share one key space. A blank key, or one holding a control character, is refused as
    /// <c>invalid-request-id</c>.
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
    /// client/server engine mismatch on real input. Absent = no check. At most
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/> bytes (UTF-8): a larger one is refused as <c>too-large</c> before
    /// it is compared. It is recorded masked, and a divergence logs only its length and SHA-256 hash.
    /// </summary>
    [Trax.Effect.Attributes.TraxSensitive]
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

    /// <summary>
    /// The instance's revision after this request, bumped by the database on every write, or <c>null</c> where the
    /// store does not record one. It travels beside <see cref="Snapshot"/>, never inside it, and orders the writes
    /// of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one. <c>null</c> where the store does not record one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <c>null</c> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// False when this answer replays a request the instance has moved past since, whose due time the server cannot
    /// know: <see cref="DueAt"/> is then null and says nothing. True on every other answer, where a null
    /// <see cref="DueAt"/> means the state has no timer or the server cleared it.
    /// </summary>
    public bool DueAtKnown { get; init; } = true;
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

    /// <summary>
    /// The instance's revision after this request, bumped by the database on every write, or <c>null</c> where the
    /// store does not record one. It travels beside <see cref="Snapshot"/>, never inside it, and orders the writes
    /// of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one. <c>null</c> where the store does not record one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <c>null</c> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
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
    /// refused as <c>request-id-reused</c>, before the effect runs. A blank key, or one holding a control
    /// character, is refused as <c>invalid-request-id</c>, also before the effect runs.
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
/// Input of the <c>stateMachine.startSnapshot</c> mutation (<see cref="StartSnapshot"/>): create a new instance of
/// a machine that declares a <c>start</c>, for the caller. The server mints the id and applies the start's declared
/// reduction to the initial context; the client never writes a snapshot.
/// </summary>
public record StartSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>unknown-machine</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>
    /// The start input as a JSON object, validated against the start's declared record (never the context type).
    /// Omit it when the start's record has no required field. At most <see cref="SnapshotLimits.MaxSnapshotBytes"/>
    /// bytes (UTF-8); a larger one is refused as <c>too-large</c> before it is parsed, and one that is not a JSON
    /// object as <c>malformed</c>.
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
    /// first request created, and the same key with a different input is refused as <c>request-id-reused</c>.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused with a
    /// <c>schema-mismatch</c> problem before anything is written. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.startSnapshot</c> mutation. Either <see cref="Id"/> and <see cref="Snapshot"/> are
/// set, or <see cref="Problem"/> is: refusals come back as data, never as a GraphQL error.
/// </summary>
public record StartSnapshotOutput
{
    /// <summary>The id the server minted for the instance; null when <see cref="Problem"/> is set.</summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// True when this request created the instance, false when it was a retry of the start that did; null when
    /// <see cref="Problem"/> is set.
    /// </summary>
    public bool? Created { get; init; }

    /// <summary>The instance's snapshot in canonical wire JSON; null when <see cref="Problem"/> is set.</summary>
    public string? Snapshot { get; init; }

    /// <summary>
    /// Why the start was refused (for example <c>unauthenticated</c>, <c>unknown-machine</c>, <c>schema-mismatch</c>,
    /// <c>no-start</c>, <c>forbidden</c>, <c>invalid-request-id</c>, <c>request-id-reused</c>,
    /// <c>too-many-drafts</c> or <c>invalid-input</c>); null on success. Nothing was written when it is set.
    /// </summary>
    public SnapshotProblem? Problem { get; init; }

    /// <summary>The instance's revision, or <c>null</c> when <see cref="Problem"/> is set.</summary>
    public long? Revision { get; init; }

    /// <summary>
    /// When the timer of the state the instance started in fires, or <c>null</c> when that state has no timer (or
    /// <see cref="Problem"/> is set). It travels beside <see cref="Snapshot"/>, never inside it.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>
/// Input of the <c>stateMachine.discardSnapshot</c> mutation (<see cref="DiscardSnapshot"/>): delete one of the
/// caller's own instances, cancelling a run its state invoked first.
/// </summary>
public record DiscardSnapshotInput
{
    /// <summary>The registered machine's name (e.g. "checkout"); an unknown name is refused as <c>unknown-machine</c>.</summary>
    public required string Machine { get; init; }

    /// <summary>The instance id, scoped to the authenticated user: another user's id answers as a missing one.</summary>
    public required Guid Id { get; init; }

    /// <summary>The idempotency key of this discard, required.</summary>
    public required string RequestId { get; init; }
}

/// <summary>
/// Result of the <c>stateMachine.discardSnapshot</c> mutation. Either <see cref="Discarded"/> is true or
/// <see cref="Problem"/> is set.
/// </summary>
public record DiscardSnapshotOutput
{
    /// <summary>
    /// True when the instance is gone, whether this request deleted it or it was already gone: a discard is
    /// idempotent, and another user's id answers exactly as a missing one does.
    /// </summary>
    public bool Discarded { get; init; }

    /// <summary>
    /// Why the discard was refused (for example <c>unauthenticated</c>, <c>unknown-machine</c>,
    /// <c>invalid-request-id</c> or <c>system-owned</c>); null on success. The instance is unchanged when it is set.
    /// </summary>
    public SnapshotProblem? Problem { get; init; }
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

    /// <summary>
    /// The instance's revision after this request, bumped by the database on every write, or <c>null</c> where the
    /// store does not record one. It travels beside <see cref="Snapshot"/>, never inside it, and orders the writes
    /// of one <see cref="RowId"/> only.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>
    /// The row's surrogate key, which names one incarnation of the instance: a draft deleted and created again under
    /// the same id has a new one. <c>null</c> where the store does not record one.
    /// </summary>
    public long? RowId { get; init; }

    /// <summary>
    /// When the timer of the state the instance is in fires, or <c>null</c> when that state has no timer or it is
    /// not recorded yet.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// False when this answer replays a request the instance has moved past since, whose due time the server cannot
    /// know: <see cref="DueAt"/> is then null and says nothing. True on every other answer, where a null
    /// <see cref="DueAt"/> means the state has no timer or the server cleared it.
    /// </summary>
    public bool DueAtKnown { get; init; } = true;
}
