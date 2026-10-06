namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;

public record TradeGridEnergyOutput
{
    public double KwhSold { get; init; }
    public decimal Revenue { get; init; }
    public required string UbossTransactionId { get; init; }
}
