using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A train whose only junction calls an upstream that never answers, through an
/// <see cref="HttpClient"/> with a short timeout. The timeout surfaces as a
/// <see cref="TaskCanceledException"/> that nothing asked for.
/// </summary>
public class HttpTimeoutSchedulerTestTrain
    : ServiceTrain<HttpTimeoutSchedulerTestInput, Unit>,
        IHttpTimeoutSchedulerTestTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<CallUnresponsiveUpstream>().Resolve();
}

/// <summary>Input for <see cref="HttpTimeoutSchedulerTestTrain"/>.</summary>
public record HttpTimeoutSchedulerTestInput : IManifestProperties
{
    public int TimeoutMilliseconds { get; set; } = 100;
}

/// <summary>Interface for <see cref="HttpTimeoutSchedulerTestTrain"/>.</summary>
public interface IHttpTimeoutSchedulerTestTrain
    : IServiceTrain<HttpTimeoutSchedulerTestInput, Unit> { }

/// <summary>Calls an upstream that never answers, so the client's own timeout fires.</summary>
internal sealed class CallUnresponsiveUpstream : Junction<HttpTimeoutSchedulerTestInput, Unit>
{
    public override async Task<Unit> Run(HttpTimeoutSchedulerTestInput input)
    {
        using var client = new HttpClient(new UnresponsiveHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(input.TimeoutMilliseconds),
        };

        await client.GetAsync("http://upstream.invalid/slow");

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
