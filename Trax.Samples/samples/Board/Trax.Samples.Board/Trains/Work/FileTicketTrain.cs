using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

/// <summary>Files a ticket from one of the reports the shop knows. It lands in Triage.</summary>
[TraxAuthorize(Roles = BoardRoles.Reporter)]
[TraxMutation(GraphQLOperation.Run, Description = "Files a ticket on the board.")]
public class FileTicketTrain : ServiceTrain<FileTicketInput, TicketView>, IFileTicketTrain
{
    protected override Task<Either<Exception, TicketView>> Junctions() =>
        Chain<FileTicket>().Resolve();
}
