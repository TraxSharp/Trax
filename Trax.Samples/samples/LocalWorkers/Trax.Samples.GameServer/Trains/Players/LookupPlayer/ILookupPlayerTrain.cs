using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Players.LookupPlayer;

public interface ILookupPlayerTrain : IServiceTrain<LookupPlayerInput, PlayerProfile>;
