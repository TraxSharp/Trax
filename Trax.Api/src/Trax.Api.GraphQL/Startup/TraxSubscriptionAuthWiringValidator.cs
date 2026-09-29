using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails fast at host startup when a token authentication scheme is registered but HotChocolate's
/// accept-everything <see cref="DefaultSocketSessionInterceptor"/> is what would answer a
/// subscription's <c>connection_init</c>.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> registers <c>TraxCompositeSocketInterceptor</c> for every host, and
/// the composite reads the registered schemes from the completed container, so registration
/// order no longer decides whether subscriptions authenticate. Before that, a scheme registered
/// after <c>AddTraxGraphQL()</c> left no interceptor wired, HotChocolate fell back to its default,
/// and subscriptions stopped authenticating while HTTP kept working.
/// <para>
/// This validator is the backstop for that failure direction: if anything ever leaves the
/// default interceptor active while a scheme is registered, the host refuses to start rather
/// than serve unauthenticated sockets. A host-supplied interceptor of any other type is the
/// host's decision and passes.
/// </para>
/// </remarks>
internal sealed class TraxSubscriptionAuthWiringValidator(
    IServiceProviderIsService isService,
    IRequestExecutorProvider executorProvider,
    string schemaName
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var executor = await executorProvider
            .GetExecutorAsync(schemaName, cancellationToken)
            .ConfigureAwait(false);

        // Exact type, not a pattern match: every real interceptor (Trax's included) derives
        // from DefaultSocketSessionInterceptor, so `is not DefaultSocketSessionInterceptor`
        // would never be true.
        var active = executor.Schema.Services.GetService<ISocketSessionInterceptor>();
        if (active is not null && active.GetType() != typeof(DefaultSocketSessionInterceptor))
            return;

        var registered = new List<string>();

        if (isService.IsService(typeof(JwtDispatcherRuntime)))
            registered.Add("AddTraxJwtDispatcher()");
        else if (
            isService.IsService(typeof(ITraxPrincipalResolver<JwtTokenInput>))
            || isService.IsService(typeof(JwtResolverRegistry))
        )
            registered.Add("AddTraxJwtAuth(...)");

        if (isService.IsService(typeof(ITraxPrincipalResolver<string>)))
            registered.Add("AddTraxApiKeyAuth(...)");

        if (registered.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{string.Join(" and ", registered)} is registered, but the subscription socket "
                + "interceptor for the Trax schema is HotChocolate's default, which accepts every "
                + "connection_init. WebSocket clients would connect without being authenticated "
                + "at all, while HTTP requests stay gated, which is what makes this easy to miss.\n"
                + "AddTraxGraphQL() registers TraxCompositeSocketInterceptor for this; something "
                + "replaced it with the default. To take over subscription auth, register your "
                + "own interceptor type through ConfigureSchema(b => "
                + "b.AddSocketSessionInterceptor<T>())."
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
