namespace Trax.Api.DTOs;

/// <summary>
/// Execution roll-up for a single train (by interface FullName): its runs broken down by state,
/// most recent and most recent successful run, and average completed duration. Backs the summary
/// cards on the dashboard's per-train detail page. Keyed by <c>metadata.Name</c>, which stores the
/// interface FullName per the naming rules.
/// </summary>
public record TrainExecutionStats(
    string TrainName,
    long Total,
    long Completed,
    long Failed,
    long InProgress,
    long Pending,
    long Cancelled,
    DateTime? LastRun,
    DateTime? LastSuccessfulRun,
    double? AverageMilliseconds
);
