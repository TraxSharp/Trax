using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.ChangeCaseData.Junctions;

namespace Trax.Samples.Recovery.Trains.ChangeCaseData;

/// <summary>
/// The page's "change the data during the backoff" button. It edits what the run reads, not the
/// manifest's input, so the retry still names the failed run to replay; the replay then refuses each
/// answer whose state no longer hashes the same, and the model is asked afresh.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxMutation(GraphQLOperation.Run, Description = "Changes the data a demo run reads")]
public class ChangeCaseDataTrain
    : ServiceTrain<ChangeCaseDataInput, ChangeCaseDataOutput>,
        IChangeCaseDataTrain
{
    protected override Task<Either<Exception, ChangeCaseDataOutput>> Junctions() =>
        Chain<EditCaseFile>().Resolve();
}
