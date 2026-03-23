using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy.Junctions;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;

/// <summary>
/// Sells excess energy back to the grid via PTC UBOSS (Utility Back Office Support System).
/// Scheduled hourly via Cron. Calculates available excess from battery and solar,
/// then submits a sell order at the configured rate ($0.14/kWh).
/// Up to 80% of battery capacity can be sold.
/// </summary>
[TraxMutation(
    GraphQLOperation.Queue,
    Description = "Submits a grid energy sell order via PTC UBOSS"
)]
[TraxBroadcast]
public class TradeGridEnergyTrain
    : ServiceTrain<TradeGridEnergyInput, TradeGridEnergyOutput>,
        ITradeGridEnergyTrain
{
    protected override TradeGridEnergyOutput Junctions() =>
        Chain<CalculateExcessJunction>().Chain<SubmitToUbossJunction>();
}
