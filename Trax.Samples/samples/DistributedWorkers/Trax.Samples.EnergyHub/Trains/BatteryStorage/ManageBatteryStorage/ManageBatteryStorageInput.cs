using Trax.Effect.Models.Manifest;

namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;

public record ManageBatteryStorageInput : IManifestProperties
{
    public required string BatteryBankId { get; init; }
    public int TargetChargePercent { get; init; } = 80;
}
