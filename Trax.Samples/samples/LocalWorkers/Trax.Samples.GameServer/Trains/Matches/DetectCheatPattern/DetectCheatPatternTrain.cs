using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern.Junctions;

namespace Trax.Samples.GameServer.Trains.Matches.DetectCheatPattern;

/// <summary>
/// Dormant dependent train — only fires when anomalies are detected during match processing.
/// Declared in the scheduler topology but never auto-fires; activated at runtime
/// via IDormantDependentContext in CheckForAnomaliesJunction.
/// </summary>
public class DetectCheatPatternTrain
    : ServiceTrain<DetectCheatPatternInput, Unit>,
        IDetectCheatPatternTrain
{
    protected override Unit Junctions() =>
        Chain<AnalyzePatternJunction>().Chain<FlagPlayerJunction>();
}
