using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>Accepts a triage when the area it names is the area of the report, and sends the ticket to Reproduce.</summary>
[TraxAuthorize(Roles = BoardRoles.Triage)]
public class TriageTrain : ServiceTrain<TriageCheck, CheckResult>, ITriageTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunTriage>().Resolve();
}
