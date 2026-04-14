namespace Trax.Samples.JobHunt.Trains.MonitorJob;

public record MonitorJobOutput
{
    public bool Changed { get; init; }
    public bool Closed { get; init; }
    public string? DiffSummary { get; init; }
}
