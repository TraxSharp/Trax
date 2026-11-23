using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>A host that answers nothing until the request is cancelled.</summary>
internal sealed class SilentHost : ITraxTransport
{
    public async Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // allowed-delay: answers only by being cancelled.
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return new TraxAnswer();
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink) =>
        throw new NotSupportedException();
}
