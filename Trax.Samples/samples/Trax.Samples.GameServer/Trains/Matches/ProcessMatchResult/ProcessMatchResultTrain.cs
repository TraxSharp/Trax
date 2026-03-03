using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Steps;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

/// <summary>
/// Multi-step match result processor. Can be queued from the API via POST /trains/queue
/// or run on a recurring schedule by the scheduler for batch reprocessing.
///
/// Steps: ValidateMatch → UpdatePlayerStats → CheckForAnomalies
///
/// When anomalies are detected, CheckForAnomaliesStep activates the dormant
/// DetectCheatPattern dependent train via IDormantDependentContext.
/// </summary>
public class ProcessMatchResultTrain
    : ServiceTrain<ProcessMatchResultInput, Unit>,
        IProcessMatchResultTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        ProcessMatchResultInput input
    ) =>
        Activate(input)
            .Chain<ValidateMatchStep>()
            .Chain<UpdatePlayerStatsStep>()
            .Chain<CheckForAnomaliesStep>()
            .Resolve();
}
