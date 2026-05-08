using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.config</c>. Returns the live scheduler runtime settings;
/// matches what the dashboard's ServerSettingsPage reads, since both go through
/// <see cref="IOperationsService"/>.
/// </summary>
public class ConfigQueries
{
    public SchedulerConfigSnapshot GetScheduler([Service] IOperationsService operationsService) =>
        operationsService.GetSchedulerConfig();
}
