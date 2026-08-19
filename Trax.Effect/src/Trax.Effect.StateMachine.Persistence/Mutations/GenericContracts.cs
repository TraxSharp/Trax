namespace Trax.Effect.StateMachine.Persistence.Mutations;

// ONE set of contracts for ALL machines. Every input carries a `machine` discriminator the resolver looks
// up in the registry, so there is no per-machine mutation fan and no per-machine CLR types. The context
// crosses the wire as opaque canonical JSON, so a single input/output shape serves every machine.

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

public record SaveSnapshotOutput
{
    public string? Snapshot { get; init; }
    public SnapshotProblem? Problem { get; init; }
}

public record AdvanceSnapshotInput
{
    public required string Machine { get; init; }
    public required Guid Id { get; init; }

    /// <summary>The trigger to fire (a machine trigger name, e.g. "Next").</summary>
    public required string Trigger { get; init; }

    /// <summary>Optional trigger input as JSON.</summary>
    public string? Input { get; init; }

    /// <summary>Optional idempotency key so a retry replays instead of re-firing.</summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch with the
    /// server's is refused with a <c>schema-mismatch</c> problem before the advance runs. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }

    /// <summary>
    /// Optional: the snapshot the client's twin computed for this advance, as canonical JSON. When present the
    /// server compares it against its own authoritative result; a divergence is refused with a
    /// <c>client-divergence</c> problem (and reported), catching a client/server engine mismatch on real input.
    /// Absent = no check. The server result is always authoritative regardless.
    /// </summary>
    public string? ClientResult { get; init; }
}

public record AdvanceSnapshotOutput
{
    public string? Snapshot { get; init; }
    public SnapshotProblem? Problem { get; init; }
}

public record LoadSnapshotInput
{
    public required string Machine { get; init; }
    public required Guid Id { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>schema-mismatch</c> problem so a stale client reloads rather than rehydrating a draft shaped
    /// by a newer contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

public record LoadSnapshotOutput
{
    public string? Snapshot { get; init; }

    /// <summary>Set when there is no such draft (normal: start fresh) or the stored data failed validation.</summary>
    public SnapshotProblem? Problem { get; init; }
}

public record SendSnapshotInput
{
    public required string Machine { get; init; }
    public required Guid Id { get; init; }

    /// <summary>Idempotency key. A stable value per intended send; if absent the draft id is used.</summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Optional: the client's machine schema hash (<see cref="IMachine.SchemaHash"/>). A mismatch is refused
    /// with a <c>schema-mismatch</c> problem so a stale client cannot trigger the irreversible send under an
    /// outdated contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}

public record SendSnapshotOutput
{
    public string? Snapshot { get; init; }
    public SnapshotProblem? Problem { get; init; }
}
