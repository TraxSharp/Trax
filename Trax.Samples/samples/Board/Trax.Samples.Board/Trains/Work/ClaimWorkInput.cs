namespace Trax.Samples.Board.Trains.Work;

public sealed record ClaimWorkInput
{
    /// <summary>The lane to claim from: triage, reproduce, fix, qa or review. It must be the caller's own.</summary>
    public string Lane { get; init; } = "";

    /// <summary>A ticket to claim; the next free one in the lane when left out.</summary>
    public string? TicketId { get; init; }
}
