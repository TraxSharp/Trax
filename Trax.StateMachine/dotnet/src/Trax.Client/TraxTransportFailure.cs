using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>A request that got no GraphQL answer: the host could not be reached, or answered with something else.</summary>
/// <param name="Message">What went wrong.</param>
/// <param name="Status">The HTTP status, when a response arrived.</param>
public sealed record TraxTransportFailure(string Message, int? Status = null)
{
    /// <summary>The code a transport failure is reported under, as the state-machine clients report it.</summary>
    public const string Code = "TRANSPORT_ERROR";

    /// <summary>
    /// The close code the host closed the socket with when it turned a stream away for good (4401 unauthorized, 4403
    /// forbidden); null for any other failure.
    /// </summary>
    public int? CloseStatus { get; init; }
}
