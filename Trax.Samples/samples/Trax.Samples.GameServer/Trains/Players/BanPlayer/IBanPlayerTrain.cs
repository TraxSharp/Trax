using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Players.BanPlayer;

public interface IBanPlayerTrain : IServiceTrain<BanPlayerInput, Unit>;
