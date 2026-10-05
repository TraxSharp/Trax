using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.SignalRBroadcaster.Trains.Ping.Junctions;

namespace Trax.Samples.SignalRBroadcaster.Trains.Ping;

public class PingTrain : ServiceTrain<PingInput, PingOutput>, IPingTrain
{
    protected override Task<Either<Exception, PingOutput>> Junctions() =>
        Chain<PingJunction>().Resolve();
}
