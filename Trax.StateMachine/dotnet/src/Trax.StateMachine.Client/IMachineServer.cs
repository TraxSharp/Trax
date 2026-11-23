using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

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
