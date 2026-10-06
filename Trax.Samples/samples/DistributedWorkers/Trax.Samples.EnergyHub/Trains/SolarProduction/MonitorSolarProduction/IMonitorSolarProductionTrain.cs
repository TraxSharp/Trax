using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

public interface IMonitorSolarProductionTrain
    : IServiceTrain<MonitorSolarProductionInput, MonitorSolarProductionOutput>;
