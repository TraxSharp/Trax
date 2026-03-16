using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage.Junctions;

public class OptimizeChargeLevelJunction(ILogger<OptimizeChargeLevelJunction> logger)
    : Junction<ManageBatteryStorageInput, ManageBatteryStorageOutput>
{
    public override async Task<ManageBatteryStorageOutput> Run(ManageBatteryStorageInput input)
    {
        logger.LogInformation(
            "[{BatteryBankId}] Optimizing charge level toward {Target}%",
            input.BatteryBankId,
            input.TargetChargePercent
        );

        await Task.Delay(200);

        var currentCharge = 67;
        var action = currentCharge < input.TargetChargePercent ? "Charging" : "Holding";

        logger.LogInformation(
            "[{BatteryBankId}] Battery action: {Action} (current: {Current}%, target: {Target}%)",
            input.BatteryBankId,
            action,
            currentCharge,
            input.TargetChargePercent
        );

        return new ManageBatteryStorageOutput
        {
            BatteryBankId = input.BatteryBankId,
            CurrentChargePercent = currentCharge,
            Action = action,
        };
    }
}
