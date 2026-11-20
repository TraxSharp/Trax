using System.Globalization;
using System.Xml;
using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Board.Trains.Work;

/// <summary>
/// A worker asks for work in its lane. The server picks the ticket (the one named, or the next free one), claims it
/// for the caller in the same guarded move that a second claim is refused by, and hands back the work package.
/// </summary>
[TraxAuthorize(Roles = BoardRoles.AnyLane)]
[TraxMutation(
    GraphQLOperation.Run,
    Description = "Claims a ticket in the caller's lane and returns its work package."
)]
public class ClaimWorkTrain : ServiceTrain<ClaimWorkInput, WorkPackage>, IClaimWorkTrain
{
    protected override Task<Either<Exception, WorkPackage>> Junctions() =>
        Chain<ClaimTicket>().Resolve();
}
