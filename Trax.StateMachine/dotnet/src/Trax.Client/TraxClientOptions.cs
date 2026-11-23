using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>How a <see cref="TraxClient"/> reads answers and follows streams.</summary>
public sealed record TraxClientOptions
{
    /// <summary>
    /// The options variables are written and data read with; <see cref="TraxJson.Options"/> when null. The client
    /// reads them with a copy that leaves every unset <see cref="Optional{T}"/> out of the JSON, whatever their
    /// resolver: a source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> included, which
    /// is what a trimmed or native AOT app passes here.
    /// </summary>
    public JsonSerializerOptions? Json { get; init; }

    /// <summary>
    /// Delays before subscribing again after the socket dropped a stream. The last repeats for as long as the stream
    /// is followed; they start over once a subscription delivers a frame. <see cref="TraxClient.DefaultResubscribeDelays"/>
    /// when null or empty. Each is a step: the wait is drawn at random between half of it and the whole, so the
    /// streams a host dropped together do not all come back together.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResubscribeDelays { get; init; }

    /// <summary>The clock the resubscribe delays run on; the system's when null.</summary>
    public TimeProvider? Time { get; init; }

    // Where each resubscribe wait is drawn from, a number from 0 to 1; the system's random numbers when null. A test pins it.
    internal Func<double>? Random { get; init; }
}
