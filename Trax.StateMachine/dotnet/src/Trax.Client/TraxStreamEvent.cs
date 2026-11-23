using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>What a followed stream delivers.</summary>
public abstract record TraxStreamEvent<T>
{
    private TraxStreamEvent() { }

    /// <summary>One frame's data.</summary>
    public sealed record Next(T Data) : TraxStreamEvent<T>;

    /// <summary>
    /// The socket dropped the stream; it is subscribed again after the resubscribe delays. Frames sent meanwhile are
    /// lost.
    /// </summary>
    public sealed record Interrupted(string Reason) : TraxStreamEvent<T>;

    /// <summary>The stream is subscribed again after an interruption: read again what it may have missed.</summary>
    public sealed record Resumed : TraxStreamEvent<T>;

    /// <summary>
    /// The stream is over and is not subscribed again. The host completed it (neither is set), refused it or answered
    /// a frame with errors (<see cref="Errors"/>); or <see cref="Failure"/> says why: the host refused the connection,
    /// there is no socket to follow it over, a frame did not fit the document's result, or the handler threw.
    /// </summary>
    public sealed record Ended(
        IReadOnlyList<TraxGraphQLError> Errors,
        TraxTransportFailure? Failure
    ) : TraxStreamEvent<T>;
}
