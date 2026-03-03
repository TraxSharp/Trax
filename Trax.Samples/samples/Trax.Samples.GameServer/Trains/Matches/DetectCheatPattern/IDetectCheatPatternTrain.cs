using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern;

public interface IDetectCheatPatternTrain : IServiceTrain<DetectCheatPatternInput, Unit>;
