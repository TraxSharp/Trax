using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage.Steps;

public class ReadBatteryStateStep(ILogger<ReadBatteryStateStep> logger)
    : Step<ManageBatteryStorageInput, ManageBatteryStorageInput>
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
