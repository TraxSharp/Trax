using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Evaluates the declared management gate against a principal with the semantics the GraphQL
/// fields get from <see cref="AuthorizeDirectives"/>: each requirement on its own, all of them
/// required, a role list any of.
/// </summary>
/// <remarks>
/// Scoped, so a policy is evaluated with the caller's services (a request, a dashboard circuit),
/// where GraphQL evaluates it, and a scoped authorization handler is resolved from that scope.
/// </remarks>
internal sealed class PersistedOperationsManagementGate(
    PersistedOperationsOptions options,
    IServiceProvider services
) : IPersistedOperationsManagementGate
{
    public bool IsDeclared => options.ManagementServed;

    public async Task<bool> AllowsAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        // Any authenticated identity, as @authorize counts one, not only the first.
        if (!options.ManagementServed || !user.Identities.Any(i => i.IsAuthenticated))
            return false;

        AuthorizeDirectives.ExtractRules(
            options.ManagementGate,
            out var policies,
            out var roleSets
        );

        // Exact, case-sensitive, as @authorize matches roles.
        if (roleSets.Any(roles => !roles.Any(user.IsInRole)))
            return false;

        if (policies.Length == 0)
            return true;

        // A host that registers no authorization cannot satisfy a policy.
        var authorization = services.GetService<IAuthorizationService>();
        if (authorization is null)
            return false;

        foreach (var policy in policies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (
                    !(
                        await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)
                    ).Succeeded
                )
                    return false;
            }
            catch (InvalidOperationException)
            {
                // A policy name the host never registered refuses, as it does in GraphQL.
                return false;
            }
        }
        return true;
    }
}
