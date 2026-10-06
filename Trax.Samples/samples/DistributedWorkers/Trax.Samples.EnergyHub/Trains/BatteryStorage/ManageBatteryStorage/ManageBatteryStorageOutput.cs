namespace Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;

public record ManageBatteryStorageOutput
{
    public required string BatteryBankId { get; init; }
    public int CurrentChargePercent { get; init; }
    public required string Action { get; init; }
}
