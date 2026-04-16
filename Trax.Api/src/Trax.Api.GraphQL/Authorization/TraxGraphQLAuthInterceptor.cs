using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.GraphQL.Authorization;

/// <summary>
/// HotChocolate <see cref="IHttpRequestInterceptor"/> that enforces a Trax
/// authorization policy on every GraphQL execution request. The interceptor
/// only runs when HotChocolate treats the inbound request as a GraphQL
/// request, so the Banana Cake Pop tool page (HTML GET) is never blocked.
/// </summary>
/// <remarks>
/// Failures surface as a GraphQL error with code <c>TRAX_AUTHORIZATION</c>
/// rather than an HTTP 401. This keeps the response shape consistent with
/// per-train authorization failures and lets the IDE render the error inline.
/// Wired by <c>AddTraxGraphQL(graphql =&gt; graphql.RequireAuthorization())</c>.
/// </remarks>
internal sealed class TraxGraphQLAuthInterceptor(
    IAuthorizationService authorization,
    GraphQLConfiguration configuration
) : DefaultHttpRequestInterceptor
{
    public override async ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken
    )
    {
        var policy = configuration.AuthorizationPolicy ?? TraxAuthClaimTypes.TraxAuthPolicy;
        var result = await authorization.AuthorizeAsync(context.User, policy);

        if (!result.Succeeded)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("Not authorized.")
                    .SetCode("TRAX_AUTHORIZATION")
                    .Build()
            );

        await base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
