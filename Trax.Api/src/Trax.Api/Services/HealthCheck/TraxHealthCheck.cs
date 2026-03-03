using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trax.Api.Services.HealthCheck;

/// <summary>
/// ASP.NET Core health check that delegates to <see cref="ITraxHealthService"/>
/// for the actual DB queries, then maps the result to a <see cref="HealthCheckResult"/>.
/// </summary>
public class TraxHealthCheck(ITraxHealthService healthService) : IHealthCheck
{
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
