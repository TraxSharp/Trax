using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Checks;

/// <summary>
/// Closes a ticket nobody could reproduce. The ticket's own guard decides: it counts the cases the server ran, not the
/// ones the agent says it tried.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.Reproducer)]
public class CloseTrain : ServiceTrain<CloseCheck, CheckResult>, ICloseTrain
{
    protected override Task<Either<Exception, CheckResult>> Junctions() =>
        Chain<RunClose>().Resolve();
}
