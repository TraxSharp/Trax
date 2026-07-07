using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.SchedulerLiveness;

/// <summary>
/// Reports unhealthy when the JobDispatcher has not completed a polling cycle within the
/// configured threshold. Unlike a process/port probe, this catches a scheduler that is up
/// but dispatching nothing (the failure mode a TCP or 200-OK check stays green through).
/// </summary>
internal sealed class SchedulerLivenessHealthCheck(
    ISchedulerLivenessMonitor monitor,
    SchedulerConfiguration configuration,
    TimeProvider timeProvider
) : IHealthCheck
{
    /// <summary>Overrides the configured threshold when set at the registration site.</summary>
    public TimeSpan? ThresholdOverride { get; init; }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var threshold =
            ThresholdOverride
            ?? configuration.SchedulerLivenessThreshold
            ?? DefaultThreshold(configuration);

        // Before the first cycle completes, measure from startup so a cold start is healthy
        // within the grace window but a never-dispatching scheduler still trips.
        var reference = monitor.LastDispatchCompletedAt ?? monitor.StartedAt;
        var age = timeProvider.GetUtcNow() - reference;

        var data = new Dictionary<string, object>
        {
            ["lastDispatchCompletedAt"] = monitor.LastDispatchCompletedAt?.ToString("O") ?? "never",
            ["ageSeconds"] = Math.Round(age.TotalSeconds, 1),
            ["thresholdSeconds"] = Math.Round(threshold.TotalSeconds, 1),
        };

        if (age <= threshold)
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"JobDispatcher completed a cycle {age.TotalSeconds:F0}s ago",
                    data
                )
            );

        return Task.FromResult(
            new HealthCheckResult(
                context.Registration.FailureStatus,
                $"JobDispatcher has not completed a cycle for {age.TotalSeconds:F0}s (threshold {threshold.TotalSeconds:F0}s)",
                data: data
            )
        );
    }

    /// <summary>
    /// The default staleness threshold when none is configured: ten dispatch cycles, floored
    /// at 30 seconds so a fast poll interval does not produce a flappy check.
    /// </summary>
    internal static TimeSpan DefaultThreshold(SchedulerConfiguration configuration)
    {
        var tenCycles = configuration.JobDispatcherPollingInterval * 10;
        var floor = TimeSpan.FromSeconds(30);
        return tenCycles > floor ? tenCycles : floor;
    }
}
