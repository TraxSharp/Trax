namespace Trax.Dashboard.Models;

public class GroupSummary
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public int? MaxActiveJobs { get; init; }
    public int Priority { get; init; }
    public bool IsEnabled { get; init; }
    public int ManifestCount { get; init; }
    public int TotalExecutions { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public DateTime? LastRun { get; init; }
}
