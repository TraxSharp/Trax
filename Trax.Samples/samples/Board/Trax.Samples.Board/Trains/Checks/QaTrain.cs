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
/// Runs QA's own cases on the rules as patched. QA's work is accepted either way: a case that breaks sends the ticket
/// back to Fix with it, and a clean sweep sends it on to Review.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Qa)]
public class QaTrain : ServiceTrain<QaCheck, CheckResult>, IQaTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunQaSweep>().Resolve();
}
