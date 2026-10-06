using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trax.Api.Services.HealthCheck;

/// <summary>
/// ASP.NET Core health check that delegates to <see cref="ITraxHealthService"/>
/// for the actual DB queries, then maps the result to a <see cref="HealthCheckResult"/>.
/// Registered by <see cref="Trax.Api.Extensions.HealthCheckExtensions.AddTraxHealthCheck"/>;
/// infrastructure not intended to be used directly.
/// </summary>
internal class TraxHealthCheck(ITraxHealthService healthService) : IHealthCheck
{
    /// <summary>
    /// Reports <c>Healthy</c> when <see cref="Trax.Api.DTOs.HealthStatus.Status"/> is <c>Healthy</c>
    /// and <c>Degraded</c> otherwise, never
    /// <c>Unhealthy</c>. The counts are attached as data under <c>queueDepth</c>, <c>inProgress</c>,
    /// <c>failedLastHour</c> and <c>deadLetters</c>.
    /// </summary>
    /// <param name="context">The health check context (unused).</param>
    /// <param name="ct">Cancels the underlying queries.</param>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default
    )
    {
        var status = await healthService.GetHealthAsync(ct);

        var data = new Dictionary<string, object>
        {
            ["queueDepth"] = status.QueueDepth,
            ["inProgress"] = status.InProgress,
            ["failedLastHour"] = status.FailedLastHour,
            ["deadLetters"] = status.DeadLetters,
        };

        return status.Status == "Healthy"
            ? HealthCheckResult.Healthy(status.Description, data: data)
            : HealthCheckResult.Degraded(status.Description, data: data);
    }
}
