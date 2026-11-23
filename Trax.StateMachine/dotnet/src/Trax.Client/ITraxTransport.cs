using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>
/// How a request reaches a host. <see cref="TraxClient"/> never talks to the network itself: it is given a
/// <see cref="TraxHttpTransport"/>, or a transport of the app's own (a recording, a fake, a wrapper that counts what is
/// in flight).
/// </summary>
public interface ITraxTransport
{
    /// <summary>
    /// Sends a query or mutation. Never throws for the host's sake: a refusal is an answer with errors, and a host that
    /// could not be reached is an answer with a <see cref="TraxAnswer.Failure"/>. Throws only when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Follows a subscription, each frame a GraphQL result, until the returned handle is disposed.</summary>
    IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink);
}
