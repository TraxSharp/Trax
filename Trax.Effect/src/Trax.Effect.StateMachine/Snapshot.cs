using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// The language-neutral, serializable state of a machine instance — the only thing that
/// crosses the wire or lands in a database row. Both the C# backend and a TypeScript client
/// read and write this exact shape:
///
/// <code>
/// { "machine": "turnstile", "version": 1, "state": "Locked", "context": { } }
/// </code>
///
/// <para><b>State</b> is the machine's state token as a string (an enum name on the C# side,
/// a string-literal union member on the TypeScript side). <b>Context</b> is the per-state data,
/// discriminated by <see cref="State"/> — the mechanism that makes an illegal (state, data)
/// pair unrepresentable. A snapshot is only ever produced by
/// <see cref="SnapshotMachine{TState,TTrigger}.Rehydrate"/> (which validates it) or by
/// <see cref="SnapshotMachine{TState,TTrigger}.Advance"/> (which produces a validated successor),
/// so a <see cref="Snapshot"/> in hand is always well-formed for its definition.</para>
/// </summary>
public sealed record Snapshot
{
    /// <summary>Identifier of the machine definition this snapshot belongs to.</summary>
    public required string Machine { get; init; }

    /// <summary>Definition version the snapshot was produced against (drives migration decisions).</summary>
    public required int Version { get; init; }

    /// <summary>The current state token.</summary>
    public required string State { get; init; }

    /// <summary>The per-state context data. Shape is discriminated by <see cref="State"/>.</summary>
    public required JsonObject Context { get; init; }

    // A JsonObject compares by reference, which would break value equality for a record whose
    // whole point is data equality. Compare context structurally so two snapshots with equal
    // JSON are equal (this is what the conformance and round-trip tests assert on).
    public bool Equals(Snapshot? other) =>
        other is not null
        && Machine == other.Machine
        && Version == other.Version
        && State == other.State
        && JsonNode.DeepEquals(Context, other.Context);

    public override int GetHashCode() => HashCode.Combine(Machine, Version, State);
}
