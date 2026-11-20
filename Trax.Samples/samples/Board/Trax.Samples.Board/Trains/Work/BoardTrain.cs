using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Shop;
using Trax.Samples.Board.Tickets;

namespace Trax.Samples.Board.Trains.Work;

/// <summary>Every ticket on the board, with its lane and the last note a lane left on it.</summary>
[TraxAuthorize(Roles = BoardRoles.AnyLane + "," + BoardRoles.Reporter)]
[TraxQuery(Namespace = "board", Description = "Lists every ticket on the board.")]
public class BoardTrain : ServiceTrain<BoardInput, BoardView>, IBoardTrain
{
    protected override Task<Either<Exception, BoardView>> Junctions() =>
        Chain<ReadBoard>().Resolve();
}
