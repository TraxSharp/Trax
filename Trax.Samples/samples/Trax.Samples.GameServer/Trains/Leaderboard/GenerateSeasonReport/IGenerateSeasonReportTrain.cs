using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Leaderboard.GenerateSeasonReport;

public interface IGenerateSeasonReportTrain : IServiceTrain<GenerateSeasonReportInput, Unit>;
