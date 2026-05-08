using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations under <c>operations.config</c>. Patches mutable scheduler runtime
/// settings; the same service powers the dashboard's ServerSettingsPage save action.
/// </summary>
public class ConfigMutations
{
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
