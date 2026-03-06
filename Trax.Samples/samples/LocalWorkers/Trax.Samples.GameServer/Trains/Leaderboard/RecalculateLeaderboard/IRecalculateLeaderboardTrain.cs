using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

public interface IRecalculateLeaderboardTrain
    : IServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>;
