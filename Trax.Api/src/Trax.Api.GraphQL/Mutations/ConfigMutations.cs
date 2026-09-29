using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations under <c>operations.config</c>. Patches mutable scheduler runtime
/// settings; the same service powers the dashboard's ServerSettingsPage save action.
/// </summary>
public class ConfigMutations
{
    /// <summary>
    /// Changes the scheduler's runtime settings. Only the fields you set change; the rest keep their
    /// current values. The change takes effect at once in this process and is saved so it
    /// survives a restart. On success <c>count</c> is the number of fields that changed. A value
    /// outside the range the scheduler can run with refuses the whole patch: the result reports
    /// failure, names each offending field, and nothing is applied.
    /// </summary>
    public async Task<OperationResponse> UpdateScheduler(
        UpdateSchedulerConfigInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.UpdateSchedulerConfigAsync(input, ct);
        return new OperationResponse(result.Success, result.Count, result.Message);
    }
}
