namespace Trax.StateMachine.Persistence.Mutations;

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
    /// When present and it differs from the server's, the request is refused with a <c>SCHEMA_MISMATCH</c>
    /// problem so a stale client reloads instead of writing under an outdated contract. Absent = no check.
    /// </summary>
    public string? SchemaHash { get; init; }
}
