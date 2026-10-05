using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.SignalRBroadcaster.Trains.Ping;

public interface IPingTrain : IServiceTrain<PingInput, PingOutput>;
