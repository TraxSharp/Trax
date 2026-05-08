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
}
