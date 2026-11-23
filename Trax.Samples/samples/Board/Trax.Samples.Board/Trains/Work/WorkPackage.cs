namespace Trax.Samples.Board.Trains.Work;

/// <summary>
/// What a worker is handed with a claim: the ticket, what its lane expects, the server's tools it may call, the
/// assignment event that delivers its evidence, and how long the claim holds. Or why there is nothing to hand it.
/// </summary>
public sealed record WorkPackage
{
    public ClaimProblem? Problem { get; init; }

    public TicketView? Ticket { get; init; }

    public string Lane { get; init; } = "";

    public string Instructions { get; init; } = "";

    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>The assignment events that deliver this lane's evidence.</summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>How long the claim holds, as an ISO 8601 duration.</summary>
    public string Deadline { get; init; } = "";
}
