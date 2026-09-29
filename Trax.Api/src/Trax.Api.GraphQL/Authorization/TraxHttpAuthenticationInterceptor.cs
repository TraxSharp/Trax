using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// The one HotChocolate HTTP request interceptor Trax installs: it establishes
/// <see cref="HttpContext.User"/> for a GraphQL HTTP request, and refuses the request early when an
/// endpoint policy is set and the user does not satisfy it.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate keeps a single <see cref="IHttpRequestInterceptor"/> per schema, so authentication
/// and the endpoint check live in one class rather than two that would replace each other.
/// </para>
/// <para>
/// <b>With an endpoint policy</b> (<c>RequireAuthorization(...)</c> on the GraphQL builder), the
/// request is authenticated with that policy's own schemes, which is what ASP.NET Core does for an
/// endpoint gated by the same policy, and the policy is then evaluated. The request pipeline
/// evaluates it again for every operation on every transport; this is the early refusal for HTTP.
/// </para>
/// <para>
/// <b>Without one</b>, a request that nothing upstream authenticated is authenticated against
/// every registered scheme, first success wins, so <c>@authorize</c> sees the caller on a host with
/// several schemes and no default. A request that matches no scheme stays anonymous and
/// <c>@authorize</c> refuses whatever it gates.
/// </para>
/// <para>
/// HotChocolate calls this only for GraphQL execution requests, so the Banana Cake Pop tool page and
/// WebSocket upgrades are unaffected. See
/// <c>docs/adr/0010-a-scheme-policy-requires-its-scheme.md</c>.
/// </para>
/// </remarks>
internal sealed class TraxHttpAuthenticationInterceptor(GraphQLConfiguration configuration)
    : DefaultHttpRequestInterceptor
{
    public override async ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken
    )
    {
        var services = context.RequestServices;

        if (configuration.AuthorizationRequired)
        {
            var policy =
                await services
                    .GetRequiredService<IAuthorizationPolicyProvider>()
                    .GetPolicyAsync(EndpointPolicy.Name(configuration))
                ?? throw new InvalidOperationException(
                    $"The GraphQL endpoint policy '{EndpointPolicy.Name(configuration)}' is not registered."
                );

            if (policy.AuthenticationSchemes.Count > 0)
                await services
                    .GetRequiredService<IPolicyEvaluator>()
                    .AuthenticateAsync(policy, context);
            else
                await AuthenticateWithAnySchemeAsync(context);

            var satisfied = await EndpointPolicy.IsSatisfiedAsync(
                services.GetRequiredService<IAuthorizationService>(),
                configuration,
                context.User
            );
            if (!satisfied)
                throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());
        }
        else
        {
            await AuthenticateWithAnySchemeAsync(context);
        }

        await base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }

    /// <summary>
    /// Leaves an already authenticated user alone; otherwise tries every registered scheme and
    /// keeps the first principal that authenticates.
    /// </summary>
    private static async Task AuthenticateWithAnySchemeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            return;

        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null)
            return;

        foreach (var scheme in await schemes.GetAllSchemesAsync())
        {
            var result = await context.AuthenticateAsync(scheme.Name);
            if (result.Succeeded && result.Principal is not null)
            {
                context.User = result.Principal;
                return;
            }
        }
    }
}
