using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;

public interface IProcessChargingSessionTrain
    : IServiceTrain<ProcessChargingSessionInput, ProcessChargingSessionOutput>;
