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

/// <summary>A refusal the server answered with, as a value. <see cref="Code"/> is the contract (<c>GUARD_FAILED</c>).</summary>
/// <param name="Code">The refusal's code as it travels.</param>
/// <param name="Message">What the server said, for a person. It is not part of the contract.</param>
public sealed record ServerProblem(string Code, string Message);

/// <summary>What the server answered a start, an event or a read with: a snapshot or a refusal, never both.</summary>
public sealed record ServerAnswer
{
    /// <summary>The instance's id, on a start's answer.</summary>
    public string? Id { get; init; }

    /// <summary>What the server stored, when it did not refuse.</summary>
    public ServerSnapshot? Snapshot { get; init; }

    /// <summary>The refusal, when it refused.</summary>
    public ServerProblem? Problem { get; init; }

    /// <summary>An answer that carries a snapshot.</summary>
    public static ServerAnswer Of(ServerSnapshot snapshot, string? id = null) =>
        new() { Snapshot = snapshot, Id = id };

    /// <summary>An answer that refuses.</summary>
    public static ServerAnswer Refused(string code, string message) =>
        new() { Problem = new ServerProblem(code, message) };
}

/// <summary>What an instance's change stream delivers.</summary>
public abstract record StreamMessage
{
    private StreamMessage() { }

    /// <summary>The instance as it is now. The first message of every new stream is one.</summary>
    public sealed record Changed(ServerSnapshot Snapshot) : StreamMessage;

    /// <summary>
    /// The instance is gone. <see cref="NotFound"/> is true when the server answered <c>NOT_FOUND</c>, which it
    /// answers alike for an instance that is missing and one that is not the caller's; false when it was deleted.
    /// </summary>
    public sealed record Gone(bool NotFound) : StreamMessage;

    /// <summary>The server asks for the instance to be read again: anything may have been missed.</summary>
    public sealed record Reload : StreamMessage;

    /// <summary>The server no longer serves this client's version of the machine.</summary>
    public sealed record ClientOutdated : StreamMessage;
}

/// <summary>
/// The operations of one machine a host serves, and its change stream: what a <see cref="ServerMachineController"/>
/// drives one instance through. A refusal is an answer; a failed request throws, and the controller reports it as a
/// transport failure. <see cref="GraphQLMachineServer"/> is the one over a host's typed <c>machines</c> root.
/// </summary>
public interface IMachineServer
{
    /// <summary>Creates an instance from the machine's start. A retry under the same request id answers the same instance.</summary>
    Task<ServerAnswer> StartAsync(
        JsonNode? input,
        string requestId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fires one event. <paramref name="clientResult"/> is the canonical wire of the snapshot the client predicted, which
    /// the server compares with its own and refuses as <c>CLIENT_DIVERGENCE</c> when they differ.
    /// </summary>
    Task<ServerAnswer> EventAsync(
        string id,
        string trigger,
        JsonNode? input,
        string requestId,
        string? clientResult,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads the instance; <c>NOT_FOUND</c> when there is none the caller may read.</summary>
    Task<ServerAnswer> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Listens to the instance's changes. The returned handle closes the stream. A stream that ends without saying why is
    /// subscribed again by the server, so <paramref name="onMessage"/> hears only of what the controller must act on.
    /// </summary>
    IDisposable Subscribe(string id, Action<StreamMessage> onMessage);
}
