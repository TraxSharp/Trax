using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.Junctions;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

/// <summary>
/// Multi-step match result processor. Can be queued from the API via GraphQL
/// or run on a recurring schedule by the scheduler for batch reprocessing.
///
/// Junctions: ValidateMatch → UpdatePlayerStats → CheckForAnomalies
///
/// Returns a typed ProcessMatchResultOutput with match processing summary.
/// When anomalies are detected, CheckForAnomaliesJunction activates the dormant
/// DetectCheatPattern dependent train via IDormantDependentContext.
/// </summary>
[TraxMutation(
    Namespace = GraphQLNamespaces.Matches,
    Description = "Processes a completed match result"
)]
[TraxBroadcast]
public class ProcessMatchResultTrain
    : ServiceTrain<ProcessMatchResultInput, ProcessMatchResultOutput>,
        IProcessMatchResultTrain
{
    protected override async Task<Either<Exception, ProcessMatchResultOutput>> RunInternal(
        ProcessMatchResultInput input
    ) =>
        Activate(input)
            .Chain<ValidateMatchJunction>()
            .Chain<UpdatePlayerStatsJunction>()
            .Chain<CheckForAnomaliesJunction>()
            .Resolve();
}
