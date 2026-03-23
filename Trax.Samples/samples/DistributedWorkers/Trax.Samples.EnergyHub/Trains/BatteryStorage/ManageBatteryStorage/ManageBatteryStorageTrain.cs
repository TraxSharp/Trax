using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage.Junctions;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;

/// <summary>
/// Manages the stationary battery bank charge/discharge cycle.
/// Depends on MonitorSolarProduction via ThenInclude — runs after
/// solar data is collected to make informed charge decisions.
/// </summary>
[TraxMutation(
    GraphQLOperation.Queue,
    Namespace = "battery",
    Description = "Manages battery bank charge/discharge cycle"
)]
[TraxBroadcast]
public class ManageBatteryStorageTrain
    : ServiceTrain<ManageBatteryStorageInput, ManageBatteryStorageOutput>,
        IManageBatteryStorageTrain
{
    protected override ManageBatteryStorageOutput Junctions() =>
        Chain<ReadBatteryStateJunction>().Chain<OptimizeChargeLevelJunction>();
}
