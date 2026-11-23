namespace Trax.Samples.Board.Trains.Work;

/// <summary>A ticket as a worker or the board page sees it.</summary>
public sealed record TicketView
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>The report, with the policy the server holds the rules to.</summary>
    public string Report { get; init; } = "";

    public string State { get; init; } = "";

    /// <summary>The lane it is in: triage, reproduce, fix, qa, review, or done.</summary>
    public string Lane { get; init; } = "";

    public string? ClaimedBy { get; init; }

    /// <summary>The last thing a lane recorded on it, and who.</summary>
    public string? Note { get; init; }

    public string? By { get; init; }
}
