using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>A socket that subscribes to a document. The returned handle ends the subscription.</summary>
public interface ISubscriptionSocket
{
    /// <summary>
    /// Subscribes with <paramref name="payload"/> as the subscribe message's payload: <c>id</c> or <c>query</c>, and
    /// <c>operationName</c> and <c>variables</c>.
    /// </summary>
    IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink);
}
