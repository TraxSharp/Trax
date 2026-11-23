namespace Trax.Samples.Board.Trains.Work;

public sealed record FileTicketInput
{
    /// <summary>The ticket's id on the board.</summary>
    public string Id { get; init; } = "";

    /// <summary>The report it is filed from (boundary, forty, threshold, hundred, fixed-already).</summary>
    public string Report { get; init; } = "";
}
