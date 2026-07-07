using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Health-check registration for scheduler liveness.
/// </summary>
public static class SchedulerLivenessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that reports unhealthy when the JobDispatcher has not
    /// completed a polling cycle within the threshold. Wire the result into your container
    /// or load-balancer probe so a wedged scheduler is replaced instead of silently idling.
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The health check name (default: <c>scheduler-liveness</c>).</param>
    /// <param name="threshold">
    /// Staleness threshold. When null, uses <see cref="SchedulerConfiguration.SchedulerLivenessThreshold"/>,
    /// falling back to max(JobDispatcherPollingInterval * 10, 30s).
    /// </param>
    /// <param name="failureStatus">The status reported when stale (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    public static IHealthChecksBuilder AddTraxSchedulerLiveness(
        this IHealthChecksBuilder builder,
        string name = "scheduler-liveness",
        TimeSpan? threshold = null,
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null
    ) =>
        builder.Add(
            new HealthCheckRegistration(
                name,
                sp => new SchedulerLivenessHealthCheck(
                    sp.GetRequiredService<ISchedulerLivenessMonitor>(),
                    sp.GetRequiredService<SchedulerConfiguration>(),
                    sp.GetRequiredService<TimeProvider>()
                )
                {
                    ThresholdOverride = threshold,
                },
                failureStatus,
                tags
            )
        );
}
