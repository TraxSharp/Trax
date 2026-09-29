using System.Security.Claims;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// Evaluates the endpoint policy set with <c>RequireAuthorization(...)</c> on the GraphQL builder
/// for every operation the Trax schema executes, before the document is even looked up.
/// </summary>
/// <remarks>
/// The check sits in HotChocolate's request pipeline, which every transport goes through: an HTTP
/// request and every operation a socket carries reach it the same way, with the principal the
/// transport established. That is what makes the policy apply identically to both. A principal
/// that is not authenticated never satisfies it.
/// See <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>.
/// </remarks>
internal static class EndpointPolicyRequestMiddleware
{
    /// <summary>The pipeline key, for ordering relative to HotChocolate's own middleware.</summary>
    public const string Key = "Trax.EndpointPolicy";

    /// <summary>Where the principal lives in a request's context data.</summary>
    private const string PrincipalKey = "ClaimsPrincipal";

    public static RequestDelegate Create(RequestDelegate next) =>
        async context =>
        {
            if (!await IsAuthorizedAsync(context).ConfigureAwait(false))
                throw new GraphQLException(NotAuthorized());

            await next(context).ConfigureAwait(false);
        };

    /// <summary>The error every transport reports when the endpoint policy is not satisfied.</summary>
    public static IError NotAuthorized() =>
        ErrorBuilder.New().SetMessage("Not authorized.").SetCode("TRAX_AUTHORIZATION").Build();

    private static async ValueTask<bool> IsAuthorizedAsync(RequestContext context)
    {
        var services = context.RequestServices;
        var configuration = services.GetService<GraphQLConfiguration>();
        if (configuration?.AuthorizationRequired != true)
            return true;

        var user =
            context.ContextData.TryGetValue(PrincipalKey, out var value)
            && value is ClaimsPrincipal principal
                ? principal
                : null;

        return await EndpointPolicy
            .IsSatisfiedAsync(
                services.GetRequiredService<IAuthorizationService>(),
                configuration,
                user
            )
            .ConfigureAwait(false);
    }
}

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
