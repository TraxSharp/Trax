using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.SignalRDashboard.Trains.Ping;

public interface IPingTrain : IServiceTrain<PingInput, PingOutput>;
