using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.StartRun.Junctions;

namespace Trax.Samples.Recovery.Trains.StartRun;

/// <summary>
/// The page's "Run" button. Trax replays decisions only on a manifest's automatic retry (or a
/// requeue), and Trax.Api has no GraphQL operation for a one-off manifest, so this mutation schedules
/// one with <c>ITraxScheduler.ScheduleOnceAsync</c>.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Starts a recovery demo run as a one-off manifest"
)]
public class StartRunTrain : ServiceTrain<StartRunInput, StartRunOutput>, IStartRunTrain
{
    protected override Task<Either<Exception, StartRunOutput>> Junctions() =>
        Chain<ScheduleDemoRun>().Resolve();
}
