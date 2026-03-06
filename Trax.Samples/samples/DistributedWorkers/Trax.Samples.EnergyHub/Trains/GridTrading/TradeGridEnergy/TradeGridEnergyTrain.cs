using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy.Steps;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;

/// <summary>
/// Sells excess energy back to the grid via PTC UBOSS (Utility Back Office Support System).
/// Scheduled hourly via Cron. Calculates available excess from battery and solar,
/// then submits a sell order at the configured rate ($0.14/kWh).
/// Up to 80% of battery capacity can be sold.
/// </summary>
[TraxMutation(
    Operations = GraphQLOperation.Queue,
    Description = "Submits a grid energy sell order via PTC UBOSS"
)]
[TraxBroadcast]
public class TradeGridEnergyTrain
    : ServiceTrain<TradeGridEnergyInput, TradeGridEnergyOutput>,
        ITradeGridEnergyTrain
{
    protected override async Task<Either<Exception, TradeGridEnergyOutput>> RunInternal(
        TradeGridEnergyInput input
    ) => Activate(input).Chain<CalculateExcessStep>().Chain<SubmitToUbossStep>().Resolve();
}
