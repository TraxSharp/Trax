namespace Trax.Samples.JobHunt.Trains.MonitorJob;

/// <summary>
/// Intermediate record flowing through the MonitorJob junction chain.
/// </summary>
public record MonitorJobContext
{
    public Guid JobId { get; init; }
    public required string JobUrl { get; init; }
    public required string FetchedContent { get; init; }
    public required string ContentHash { get; init; }
    public bool Closed { get; init; }
    public string? PreviousHash { get; init; }
    public string? DiffSummary { get; init; }
    public bool Changed { get; init; }
}
