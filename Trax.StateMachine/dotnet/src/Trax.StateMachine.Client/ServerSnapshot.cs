using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>
/// One snapshot of an instance as the server stored it, with the values that travel beside it rather than in its
/// canonical wire.
/// </summary>
public sealed record ServerSnapshot
{
    /// <summary>
    /// The row's incarnation: deleting the instance and creating it again gives a new one, which restarts
    /// <see cref="Revision"/>. Null when the answer does not carry it (an event's or a read's answer); the controller
    /// then takes it to be the incarnation it holds.
    /// </summary>
    public string? RowId { get; init; }

    /// <summary>
    /// Bumped by the database on every write of the instance. Null when the store records none: the snapshot cannot be
    /// ordered, so it is never dropped as stale, and whatever comes after it is taken.
    /// </summary>
    public required long? Revision { get; init; }

    /// <summary>The state the instance is in.</summary>
    public required string State { get; init; }

    /// <summary>The state's context, with no member whose value is null.</summary>
    public required JsonObject Context { get; init; }

    /// <summary>
    /// Whether the answer knows when the state's timer is due. False when it does not carry the due time, or cannot
    /// know it (a replay of a request the instance has moved past); <see cref="DueAt"/> then means nothing.
    /// </summary>
    public bool DueAtKnown { get; init; }

    /// <summary>When the state's timer is due, by the database clock; null when the state has no timer or it was cleared.</summary>
    public DateTimeOffset? DueAt { get; init; }
}
