using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations under <c>operations.manifestGroups</c>. Patches mutable fields on a manifest
/// group (max active jobs, priority, enabled) and enables or disables groups in bulk. Thin wrapper around
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
        return ToResponse(result);
    }

    /// <summary>
    /// Enables or disables the listed manifest groups by id. Only groups whose flag differs are
    /// written, with <c>updatedAt</c> bumped; <c>count</c> is the number changed, zero included.
    /// An empty list, or more than 1000 ids, returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> SetManifestGroupsEnabled(
        long[] ids,
        bool enabled,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.SetManifestGroupsEnabledAsync(ids, enabled, ct));

    /// <summary>
    /// Enables or disables every manifest group. A field of its own so that "all" is never what an
    /// empty or missing list means. <c>count</c> is the number changed, zero included.
    /// </summary>
    public async Task<OperationResponse> SetAllManifestGroupsEnabled(
        bool enabled,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.SetAllManifestGroupsEnabledAsync(enabled, ct));

    private static OperationResponse ToResponse(OperationResult result) =>
        new(result.Success, result.Count, result.Message) { Id = result.Id };
}
