using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers;

public interface ICleanupInactivePlayersTrain : IServiceTrain<CleanupInactivePlayersInput, Unit>;
