using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

public interface IBoardTrain : IServiceTrain<BoardInput, BoardView>;

/// <summary>Every ticket on the board, with its lane and the last note a lane left on it.</summary>
[TraxAuthorize(Roles = BoardRoles.AnyLane + "," + BoardRoles.Reporter)]
[TraxQuery(Namespace = "board", Description = "Lists every ticket on the board.")]
public class BoardTrain : ServiceTrain<BoardInput, BoardView>, IBoardTrain
{
    protected override Task<Either<Exception, BoardView>> Junctions() =>
        Chain<ReadBoard>().Resolve();
}

public class ReadBoard(TicketBoard board) : Junction<BoardInput, BoardView>
{
    public override async Task<BoardView> Run(BoardInput input) =>
        new() { Tickets = (await board.All(CancellationToken)).Select(Lanes.View).ToList() };
}

public interface IChargeTrain : IServiceTrain<ChargeInput, ChargeOutput>;

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

public class ReadCharge(TicketBoard board) : Junction<ChargeInput, ChargeOutput>
{
    public override async Task<ChargeOutput> Run(ChargeInput input)
    {
        var ticket =
            await board.Find(input.TicketId, CancellationToken)
            ?? throw new ArgumentException($"There is no ticket {input.TicketId}.");
        return new ChargeOutput
        {
            Subtotal = input.Subtotal,
            Shipping = ticket.Report.Current.Charge(input.Subtotal),
        };
    }
}

public interface IFileTicketTrain : IServiceTrain<FileTicketInput, TicketView>;

/// <summary>Files a ticket from one of the reports the shop knows. It lands in Triage.</summary>
[TraxAuthorize(Roles = BoardRoles.Reporter)]
[TraxMutation(GraphQLOperation.Run, Description = "Files a ticket on the board.")]
public class FileTicketTrain : ServiceTrain<FileTicketInput, TicketView>, IFileTicketTrain
{
    protected override Task<Either<Exception, TicketView>> Junctions() =>
        Chain<FileTicket>().Resolve();
}

public class FileTicket(TicketBoard board) : Junction<FileTicketInput, TicketView>
{
    public override async Task<TicketView> Run(FileTicketInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Id) || input.Id.Length > 40)
            throw new ArgumentException("A ticket id is 1 to 40 characters.");
        if (!Reports.All.TryGetValue(input.Report, out var report))
            throw new ArgumentException(
                $"There is no report {input.Report}; the shop knows {string.Join(", ", Reports.All.Keys)}."
            );
        return Lanes.View(await board.File(input.Id, report, CancellationToken));
    }
}
