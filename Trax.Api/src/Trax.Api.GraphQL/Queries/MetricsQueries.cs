using Trax.Api.Services.Metrics;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.metrics</c>. Backed entirely by
/// <see cref="IOperationsService"/> so the dashboard's KPI cards / charts and the
/// GraphQL API see identical numbers.
/// </summary>
public class MetricsQueries
{
    public async Task<DashboardMetrics> GetDashboard(
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        MetricsRange range = MetricsRange.Last24Hours,
        bool hideAdminTrains = false
    )
    {
        return await operationsService.GetDashboardMetricsAsync(range, hideAdminTrains, ct);
    }

    public ServerMetrics GetServer([Service] IOperationsService operationsService)
    {
        return operationsService.GetServerMetrics();
    }

    /// <summary>
    /// This API process's CPU utilisation since the previous poll (0-100, normalised by core
    /// count). Separate from <see cref="GetServer"/> because it is stateful per process: it needs
    /// a delta between two samples, so the first poll returns <c>null</c> while the baseline primes.
    /// </summary>
    public double? GetServerCpuPercent([Service] ProcessCpuSampler sampler)
    {
        return sampler.SamplePercent();
    }
}
