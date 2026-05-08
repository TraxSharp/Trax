using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations under <c>operations.manifestGroups</c>. Patches mutable fields on a manifest
/// group (max active jobs, priority, enabled). Thin wrapper around
/// <see cref="IOperationsService.UpdateManifestGroupAsync"/> so the dashboard and API
/// share validation and persistence.
/// </summary>
public class ManifestGroupMutations
{
    /// <summary>
    /// Patches mutable settings on a manifest group. Each field on <paramref name="input"/>
    /// is independent; passing <c>null</c> leaves a field unchanged.
    /// </summary>
    public async Task<OperationResponse> UpdateManifestGroup(
        long id,
        UpdateManifestGroupInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.UpdateManifestGroupAsync(id, input, ct);
        return new OperationResponse(result.Success, result.Count, result.Message);
    }
}
