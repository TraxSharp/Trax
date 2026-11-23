using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

public class ReadBoard(TicketBoard board) : Junction<BoardInput, BoardView>
{
    public override async Task<BoardView> Run(BoardInput input) =>
        new()
        {
            Tickets = (await board.All(CancellationToken).ConfigureAwait(false))
                .Select(Lanes.View)
                .ToList(),
        };
}
