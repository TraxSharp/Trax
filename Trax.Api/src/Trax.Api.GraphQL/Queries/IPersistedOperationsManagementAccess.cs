using System.Security.Claims;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Whether a caller may use the <c>persistedOperations</c> root, for
/// <see cref="ConfigQueries.GetCanManagePersistedOperations"/>. Registered, scoped, by
/// <c>UsePersistedOperations</c> over the management gate it declares; a host without it registers
/// none, and the field answers false.
/// </summary>
internal interface IPersistedOperationsManagementAccess
{
    /// <summary>
    /// Whether the host serves the management fields and <paramref name="user"/> meets their gate.
    /// </summary>
    Task<bool> AllowsAsync(ClaimsPrincipal user, CancellationToken ct);
}
