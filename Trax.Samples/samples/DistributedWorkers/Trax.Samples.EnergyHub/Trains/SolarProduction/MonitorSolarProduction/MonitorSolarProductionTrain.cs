using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction.Steps;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

/// <summary>
/// Reads sensor data from the Solar PV Array and calculates production metrics.
/// Scheduled every 5 minutes. ManageBatteryStorage depends on this via ThenInclude.
/// </summary>
[TraxQuery(Description = "Reads current solar PV array production metrics")]
[TraxBroadcast]
public class MonitorSolarProductionTrain
    : ServiceTrain<MonitorSolarProductionInput, MonitorSolarProductionOutput>,
        IMonitorSolarProductionTrain
{
    protected override async Task<Either<Exception, MonitorSolarProductionOutput>> RunInternal(
        MonitorSolarProductionInput input
    ) => Activate(input).Chain<ReadSolarSensorsStep>().Chain<CalculateOutputStep>().Resolve();
}
