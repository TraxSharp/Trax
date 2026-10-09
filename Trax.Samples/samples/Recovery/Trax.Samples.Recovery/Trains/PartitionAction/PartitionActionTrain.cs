using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.PartitionAction.Junctions;

namespace Trax.Samples.Recovery.Trains.PartitionAction;

/// <summary>
/// The operator's retry button for a partition. A <c>source-partition</c> instance belongs to the
/// system, so no user's <c>stateMachine</c> mutation reaches it and the operations view of it is
/// read-only. This mutation is the sample's own, for operators only: it fires the trigger through
/// <c>IMachineInstances.Advance</c>, as the system, with the same checks a user's advance gets (a
/// retry only from <c>Failed</c> or <c>Cancelled</c>; never an outcome).
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Retries or approves one partition's ingest, as the system"
)]
public class PartitionActionTrain
    : ServiceTrain<PartitionActionInput, PartitionActionOutput>,
        IPartitionActionTrain
{
    protected override Task<Either<Exception, PartitionActionOutput>> Junctions() =>
        Chain<AdvancePartition>().Resolve();
}
