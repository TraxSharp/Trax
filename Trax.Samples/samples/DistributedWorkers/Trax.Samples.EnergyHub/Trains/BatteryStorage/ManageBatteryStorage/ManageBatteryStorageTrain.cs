using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage.Steps;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;

/// <summary>
/// Manages the stationary battery bank charge/discharge cycle.
/// Depends on MonitorSolarProduction via ThenInclude — runs after
/// solar data is collected to make informed charge decisions.
/// </summary>
[TraxMutation(
    Operations = GraphQLOperation.Queue,
    Description = "Manages battery bank charge/discharge cycle"
)]
[TraxBroadcast]
public class ManageBatteryStorageTrain
    : ServiceTrain<ManageBatteryStorageInput, ManageBatteryStorageOutput>,
        IManageBatteryStorageTrain
{
    protected override async Task<Either<Exception, ManageBatteryStorageOutput>> RunInternal(
        ManageBatteryStorageInput input
    ) => Activate(input).Chain<ReadBatteryStateStep>().Chain<OptimizeChargeLevelStep>().Resolve();
}
