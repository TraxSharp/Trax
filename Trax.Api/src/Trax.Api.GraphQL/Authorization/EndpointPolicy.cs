using System.Security.Claims;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// The endpoint policy rule, shared by the request pipeline and the socket handshake so both say
/// the same thing.
/// </summary>
internal static class EndpointPolicy
{
    /// <summary>
    /// The policy <c>RequireAuthorization(...)</c> named, or the combined Trax policy when it named
    /// none.
    /// </summary>
    public static string Name(GraphQLConfiguration configuration) =>
        configuration.AuthorizationPolicy ?? TraxAuthClaimTypes.TraxAuthPolicy;

    /// <summary>
    /// True when the endpoint is not gated, or <paramref name="user"/> is authenticated and
    /// satisfies the policy.
    /// </summary>
    public static async ValueTask<bool> IsSatisfiedAsync(
        IAuthorizationService authorization,
        GraphQLConfiguration configuration,
        ClaimsPrincipal? user
    )
    {
        if (!configuration.AuthorizationRequired)
            return true;

        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var result = await authorization
            .AuthorizeAsync(user, Name(configuration))
            .ConfigureAwait(false);
        return result.Succeeded;
    }
}
