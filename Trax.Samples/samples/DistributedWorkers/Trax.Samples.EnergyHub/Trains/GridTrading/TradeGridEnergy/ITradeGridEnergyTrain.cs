using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;

public interface ITradeGridEnergyTrain : IServiceTrain<TradeGridEnergyInput, TradeGridEnergyOutput>;
