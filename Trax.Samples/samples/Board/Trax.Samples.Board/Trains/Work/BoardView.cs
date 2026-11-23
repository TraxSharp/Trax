namespace Trax.Samples.Board.Trains.Work;

public sealed record BoardView
{
    public IReadOnlyList<TicketView> Tickets { get; init; } = [];
}
