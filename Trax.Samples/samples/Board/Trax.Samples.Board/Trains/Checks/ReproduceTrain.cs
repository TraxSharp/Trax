using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;
using static Trax.Samples.Board.Shop.ShippingRules;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>
/// Runs a reproducer's case against the shop's rules as they are today. The case reproduces the bug when the policy
/// says one charge and the rules charge another; the expected charge the agent sends must be the policy's.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Reproducer)]
public class ReproduceTrain : ServiceTrain<ReproCheck, CheckResult>, IReproduceTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunReproCase>().Resolve();
}
