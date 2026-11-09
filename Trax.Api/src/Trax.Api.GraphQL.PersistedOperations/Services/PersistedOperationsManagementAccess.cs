using System.Security.Claims;
using Trax.Api.GraphQL.Queries;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Answers <c>operations.config.canManagePersistedOperations</c> with the management gate, the one
/// the <c>persistedOperations</c> fields carry, so the field and the fields agree on who is let in.
/// </summary>
internal sealed class PersistedOperationsManagementAccess(IPersistedOperationsManagementGate gate)
    : IPersistedOperationsManagementAccess
{
    public Task<bool> AllowsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        gate.AllowsAsync(user, ct);
}
