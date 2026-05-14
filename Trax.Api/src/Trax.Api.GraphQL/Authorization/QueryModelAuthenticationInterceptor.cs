using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// HotChocolate HTTP request interceptor that populates <see cref="HttpContext.User"/>
/// for inbound GraphQL requests by attempting authentication against every registered
/// scheme until one succeeds. Wired automatically when at least one <c>[TraxQueryModel]</c>
/// entity carries <c>[TraxAuthorize]</c>.
///
/// <para>
/// Without this, HotChocolate's <c>@authorize</c> directive evaluates against an
/// anonymous principal whenever no default authentication scheme is configured on
/// <c>AddAuthentication()</c>. ASP.NET Core's <c>UseAuthentication()</c> middleware
/// only authenticates the default scheme; with multiple schemes registered (api-key
/// + JWT, api-key + cookie, etc.) there is no default, so HC sees no user even when
/// the request carries valid credentials for one of the registered schemes.
/// </para>
///
/// <para>
/// The interceptor:
/// </para>
/// <list type="number">
/// <item>Returns early if <c>HttpContext.User</c> is already authenticated (something
/// upstream — endpoint-level <c>RequireAuthorization</c>, a default scheme, a custom
/// interceptor — has handled it).</item>
/// <item>Otherwise, iterates over every registered authentication scheme and attempts
/// authentication. The first scheme that succeeds wins; the resulting principal is
/// assigned to <c>HttpContext.User</c>.</item>
/// <item>If no scheme succeeds, the principal stays anonymous and the request
/// proceeds — <c>@authorize</c> will then reject any gated field/type the request
/// touches.</item>
/// </list>
///
/// <para>
/// WebSocket upgrades and the Banana Cake Pop tool page are not affected: HotChocolate
/// invokes this interceptor only for actual GraphQL HTTP execution requests.
/// </para>
/// </summary>
internal sealed class QueryModelAuthenticationInterceptor(
    IAuthenticationSchemeProvider schemeProvider
) : DefaultHttpRequestInterceptor
{
    public override async ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken
    )
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            // Walk every registered scheme and attempt authentication. The first
            // success wins. AuthenticateAsync against an inapplicable scheme returns
            // NoResult (cheap) — schemes only do work when their credential header
            // is present.
            foreach (var scheme in await schemeProvider.GetAllSchemesAsync())
            {
                var result = await context.AuthenticateAsync(scheme.Name);
                if (result.Succeeded && result.Principal is not null)
                {
                    context.User = result.Principal;
                    break;
                }
            }
        }

        await base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
