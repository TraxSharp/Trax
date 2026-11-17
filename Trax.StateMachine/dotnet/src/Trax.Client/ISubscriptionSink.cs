using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>What a subscription delivers: one GraphQL result per event, then an error or the end.</summary>
public interface ISubscriptionSink
{
    /// <summary>One event: a GraphQL result, <c>data</c> or <c>errors</c>.</summary>
    void Next(JsonObject value);

    /// <summary>
    /// The subscription failed: the server refused it (<see cref="SubscriptionErrorException"/>), the socket closed or
    /// broke (<see cref="SocketClosedException"/> and others), or there is no socket to follow it over
    /// (<see cref="SocketUnavailableException"/>).
    /// </summary>
    void Error(Exception error);

    /// <summary>The server ended the subscription.</summary>
    void Complete();

    /// <summary>
    /// The subscribe message went out over an acknowledged connection. Nothing by default. A
    /// <see cref="GraphQLSubscriptionSocket"/> calls it before the subscription's first <see cref="Next"/>, and never
    /// calls one subscription's sink from two threads at once.
    /// </summary>
    void Started() { }
}
