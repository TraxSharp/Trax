using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage.Junctions;

public class ReadBatteryStateJunction(ILogger<ReadBatteryStateJunction> logger)
    : Junction<ManageBatteryStorageInput, ManageBatteryStorageInput>
{
    public override async Task<ManageBatteryStorageInput> Run(ManageBatteryStorageInput input)
    {
        logger.LogInformation(
            "[{BatteryBankId}] Reading battery bank state — target charge: {Target}%",
            input.BatteryBankId,
            input.TargetChargePercent
        );

        await Task.Delay(200);

        logger.LogInformation(
            "[{BatteryBankId}] Battery state: 67% charge, 45.2°C temperature, healthy",
            input.BatteryBankId
        );

        return input;
    }
}
