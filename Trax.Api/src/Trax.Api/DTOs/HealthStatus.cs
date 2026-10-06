namespace Trax.Api.DTOs;

/// <summary>
/// A point-in-time health summary of the Trax system, computed from the shared store on each
/// request. <see cref="Status"/> is <c>Degraded</c> when any dead letter awaits intervention
/// or more than 10 executions failed in the last hour, and <c>Healthy</c> otherwise.
/// </summary>
/// <param name="Status"><c>Healthy</c> or <c>Degraded</c>.</param>
/// <param name="Description">A one-line human-readable explanation of <paramref name="Status"/>.</param>
/// <param name="QueueDepth">How many work queue entries are queued and not yet dispatched.</param>
/// <param name="InProgress">How many executions are running now.</param>
/// <param name="FailedLastHour">How many executions failed with an end time in the last hour.</param>
/// <param name="DeadLetters">How many dead letters await intervention.</param>
public record HealthStatus(
    string Status,
    string Description,
    int QueueDepth,
    int InProgress,
    int FailedLastHour,
    int DeadLetters
);
