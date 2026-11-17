using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>
/// The socket closed, before it was acknowledged or while subscribed, or the host refused to open it.
/// <see cref="CloseStatus"/> is the code the server closed it with, when it sent one: 4401 (unauthorized) or 4403
/// (forbidden) is the host refusing the connection, and subscribing again will not change its answer. So is an upgrade
/// answered 401 or 403 (<see cref="HttpStatus"/>). Any other close, 4408 (the connection was not initialised in time)
/// and 4429 (too many initialisation requests) among them, may be answered differently next time.
/// </summary>
public sealed class SocketClosedException : IOException
{
    /// <summary>Builds the exception.</summary>
    public SocketClosedException(string message, int? closeStatus, int? httpStatus = null)
        : base(message)
    {
        CloseStatus = closeStatus;
        HttpStatus = httpStatus;
    }

    /// <summary>The close code the server sent, or null when the socket closed without one.</summary>
    public int? CloseStatus { get; }

    /// <summary>The HTTP status the host refused the upgrade with, or null when the socket opened.</summary>
    public int? HttpStatus { get; }

    /// <summary>
    /// Whether the host refused the connection itself (a 4401 or 4403 close, or an upgrade answered 401 or 403), so a
    /// retry gets the same answer.
    /// </summary>
    public bool Refused => CloseStatus is 4401 or 4403 || HttpStatus is 401 or 403;
}
