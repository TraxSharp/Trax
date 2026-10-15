using System.Security.Claims;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The persisted-operation management gate the host declared with <c>GateManagement(...)</c>, for a
/// surface that calls <see cref="IPersistedOperationsService"/> on a person's behalf without going
/// through GraphQL, such as the Blazor dashboard. The GraphQL <c>persistedOperations</c> fields carry
/// the same requirements, so a caller this gate refuses is refused there too.
/// </summary>
/// <remarks>
/// Registered by <c>UsePersistedOperations</c> and <c>AddPersistedOperationStore</c>, scoped: resolve
/// it from the caller's scope (the request, the dashboard's circuit), where its policies are
/// evaluated, as GraphQL evaluates the fields' in the request's. A process that
/// calls the service with no person behind it (a deploy step, a migration) does not ask the gate.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/persisted-operations/i-persisted-operations-service">SDK reference</seealso>
public interface IPersistedOperationsManagementGate
{
    /// <summary>
    /// Whether the host declared who may manage documents. When false no surface manages them: the
    /// GraphQL fields are not served and a dashboard shows no management pages.
    /// </summary>
    bool IsDeclared { get; }

    /// <summary>
    /// Whether <paramref name="user"/> meets every requirement of the gate: any of its identities
    /// authenticated, every policy satisfied, and at least one role of each role list held. False
    /// when no gate is declared, and when a requirement cannot be evaluated.
    /// </summary>
    Task<bool> AllowsAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
