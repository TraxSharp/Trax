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
