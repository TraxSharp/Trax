using Trax.Api.Services.Metrics;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.metrics</c>. Backed entirely by
/// <see cref="IOperationsService"/> so the dashboard's KPI cards / charts and the
/// GraphQL API see identical numbers.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/queries/metrics">SDK reference</seealso>
public class MetricsQueries
{
    /// <summary>
    /// The dashboard's headline counts, the executions-over-time chart for <c>range</c>, and the
    /// seven-day top failures, slowest trains and throughput series: the same numbers the
    /// dashboard's overview shows.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="range">The window and bucket size of the time series.</param>
    /// <param name="hideAdminTrains">Leave the scheduler's own internal trains out of the counts.</param>
    public async Task<DashboardMetrics> GetDashboard(
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        MetricsRange range = MetricsRange.Last24Hours,
        bool hideAdminTrains = false
    )
    {
        return await operationsService.GetDashboardMetricsAsync(range, hideAdminTrains, ct);
    }

    /// <summary>The error code a series with a missing or wrong argument is refused with.</summary>
    public const string SeriesArgumentCode = "TRAX_METRICS_SERIES_ARGUMENT";

    /// <summary>
    /// Runs over time for one train, manifest or group: for each bucket of <c>range</c>, how many
    /// runs that started in it completed, failed or were cancelled, and the median and 95th
    /// percentile duration of the completed ones. Every bucket is returned, oldest first.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="scope">What the runs are of.</param>
    /// <param name="range">The window: the last hour in minutes, 24 hours in quarter hours, or 7 days in hours. LAST30_DAYS is refused with TRAX_METRICS_SERIES_ARGUMENT: the existing indexes cannot serve it inside the metrics budget.</param>
    /// <param name="id">The manifest's or group's id, for a MANIFEST or GROUP scope.</param>
    /// <param name="name">The train's interface FullName, for a TRAIN scope.</param>
    public async Task<MetricSeries> GetSeries(
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        MetricSeriesScope scope,
        MetricSeriesRange range,
        long? id = null,
        string? name = null
    )
    {
        var refusal =
            scope == MetricSeriesScope.Train && string.IsNullOrWhiteSpace(name)
                ? "A TRAIN series needs name: the train's interface FullName."
            : scope != MetricSeriesScope.Train && id is null
                ? $"A {scope.ToString().ToUpperInvariant()} series needs id."
            : OperationsService.MetricSeriesRefusal(scope, range);
        if (refusal is not null)
            throw new GraphQLException(
                ErrorBuilder.New().SetMessage(refusal).SetCode(SeriesArgumentCode).Build()
            );

        return await operationsService.GetMetricSeriesAsync(scope, id, name, range, ct);
    }

    /// <summary>
    /// Runtime metrics of this API process: start time, uptime, working set and managed heap
    /// size, read when called. CPU is on <c>serverCpuPercent</c>.
    /// </summary>
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
