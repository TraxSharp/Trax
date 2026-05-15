using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.SignalRDashboard.Trains.Ping.Junctions;

namespace Trax.Samples.SignalRDashboard.Trains.Ping;

public class PingTrain : ServiceTrain<PingInput, PingOutput>, IPingTrain
{
    protected override Task<Either<Exception, PingOutput>> Junctions() =>
        Chain<PingJunction>().Resolve();
}
