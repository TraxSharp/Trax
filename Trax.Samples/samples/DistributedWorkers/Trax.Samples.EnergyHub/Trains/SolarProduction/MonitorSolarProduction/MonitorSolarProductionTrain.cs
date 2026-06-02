using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction.Junctions;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

/// <summary>
/// Reads sensor data from the Solar PV Array and calculates production metrics.
/// Scheduled every 5 minutes. ManageBatteryStorage depends on this via ThenInclude.
/// </summary>
[TraxAllowAnonymous]
[TraxQuery(Namespace = "solar", Description = "Reads current solar PV array production metrics")]
[TraxBroadcast]
public class MonitorSolarProductionTrain
    : ServiceTrain<MonitorSolarProductionInput, MonitorSolarProductionOutput>,
        IMonitorSolarProductionTrain
{
    protected override Task<Either<Exception, MonitorSolarProductionOutput>> Junctions() =>
        Chain<ReadSolarSensorsJunction>().Chain<CalculateOutputJunction>().Resolve();
}
