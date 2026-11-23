using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Calls an upstream that never answers, so the client's own timeout fires.</summary>
internal sealed class CallUnresponsiveUpstream : Junction<HttpTimeoutSchedulerTestInput, Unit>
{
    public override async Task<Unit> Run(HttpTimeoutSchedulerTestInput input)
    {
        using var client = new HttpClient(new UnresponsiveHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(input.TimeoutMilliseconds),
        };

        await client
            .GetAsync("http://upstream.invalid/slow", CancellationToken.None)
            .ConfigureAwait(false);

        return Unit.Default;
    }

    private sealed class UnresponsiveHandler : HttpMessageHandler
    {
        // Never answers: the only way out is the client's own timeout cancelling the token.
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(cancellationToken);
    }
}
