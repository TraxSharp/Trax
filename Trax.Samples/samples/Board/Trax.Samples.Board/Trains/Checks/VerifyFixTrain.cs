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
/// Applies a fixer's patch to the shop's rules and runs the ticket's repro case and the cases earlier fixes were
/// checked against on the result.
/// The patch is accepted only if each one now charges what the policy says, and the ticket goes to QA.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Fixer)]
public class VerifyFixTrain : ServiceTrain<FixCheck, CheckResult>, IVerifyFixTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunFixCheck>().Resolve();
}
