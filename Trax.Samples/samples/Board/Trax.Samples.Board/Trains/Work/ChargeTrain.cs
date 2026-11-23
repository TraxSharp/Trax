using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

/// <summary>
/// A tool a worker's package names: what an order pays for shipping under a ticket's rules as they are today. It
/// reads; it never moves the ticket.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.AnyLane)]
[TraxQuery(
    Namespace = "board",
    Description = "What an order pays for shipping under a ticket's rules today."
)]
public class ChargeTrain : ServiceTrain<ChargeInput, ChargeOutput>, IChargeTrain
{
    protected override Task<Either<Exception, ChargeOutput>> Junctions() =>
        Chain<ReadCharge>().Resolve();
}
