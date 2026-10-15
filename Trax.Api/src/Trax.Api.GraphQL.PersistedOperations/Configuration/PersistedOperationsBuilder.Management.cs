using Trax.Api.GraphQL.Configuration;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Serves the management fields, <c>persistedOperations { ... }</c> at the root of the query
    /// and mutation types, behind a gate of their own: upload, deactivate, restore, list and
    /// history. Without a call to this or <see cref="GateManagementToAuthenticatedUsers"/> the
    /// fields are not in the schema.
    /// </summary>
    /// <remarks>
    /// The gate is separate from <c>GateOperations</c> on purpose. A deploy pipeline that uploads
    /// documents needs this credential and none of the admin surface under <c>operations</c>, and
    /// an operator at the dashboard who manages documents needs both. A policy or roles is
    /// required, as for <c>GateOperations</c>.
    /// <para>
    /// Repeated calls accumulate, each a requirement of its own the caller must meet. Within one
    /// call, a comma-separated <paramref name="roles"/> list is any of.
    /// </para>
    /// </remarks>
    /// <param name="policy">An ASP.NET Core authorization policy name that must be satisfied.</param>
    /// <param name="roles">A comma-separated list of roles. The caller must hold at least one.</param>
    /// <exception cref="InvalidOperationException">
    /// Neither <paramref name="policy"/> nor <paramref name="roles"/> is given, or
    /// <paramref name="roles"/> names no role.
    /// </exception>
    public PersistedOperationsBuilder GateManagement(string? policy = null, string? roles = null)
    {
        if (string.IsNullOrWhiteSpace(policy) && string.IsNullOrWhiteSpace(roles))
            throw new InvalidOperationException(
                "GateManagement() needs a policy or roles: GateManagement(policy: \"...\") or "
                    + "GateManagement(roles: \"...\"). The persisted-operation management fields "
                    + "decide which documents the endpoint runs, so they do not default to any "
                    + "signed-in caller. If any authenticated user is really who should reach them, "
                    + "call GateManagementToAuthenticatedUsers()."
            );

        if (!string.IsNullOrWhiteSpace(roles) && AuthorizeDirectives.ParseRoles(roles).Length == 0)
            throw new InvalidOperationException(
                $"GateManagement(roles: \"{roles}\") names no role: the list holds only separators "
                    + "and whitespace. Name one or more roles, or call "
                    + "GateManagementToAuthenticatedUsers()."
            );

        _managementGate.Add(
            new TraxAuthorizeAttribute
            {
                Policy = string.IsNullOrWhiteSpace(policy) ? null : policy,
                Roles = string.IsNullOrWhiteSpace(roles) ? null : roles,
            }
        );
        return this;
    }

    /// <summary>
    /// Serves the management fields to any authenticated caller, with no policy or role. On a host
    /// where anyone can sign up, that is anyone, and each of them can change which documents the
    /// endpoint runs. Prefer <see cref="GateManagement"/>; use this when every authenticated
    /// principal really is trusted with documents, such as a host whose only identities are
    /// service accounts.
    /// </summary>
    public PersistedOperationsBuilder GateManagementToAuthenticatedUsers()
    {
        _managementGate.Add(new TraxAuthorizeAttribute());
        return this;
    }

    /// <summary>The management requirements declared so far.</summary>
    internal IReadOnlyList<TraxAuthorizeAttribute> ManagementGate => _managementGate;

    /// <summary>Adds requirements another builder already validated.</summary>
    internal PersistedOperationsBuilder AddManagementGate(
        IEnumerable<TraxAuthorizeAttribute> requirements
    )
    {
        _managementGate.AddRange(requirements);
        return this;
    }
}
