using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>
/// There is no socket to follow a subscription over: the transport has none, or the socket was disposed. Subscribing
/// again gets the same answer.
/// </summary>
public sealed class SocketUnavailableException : Exception
{
    /// <summary>Builds the exception.</summary>
    public SocketUnavailableException(string message)
        : base(message) { }
}
