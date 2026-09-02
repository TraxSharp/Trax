using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails fast at host startup when an authentication scheme is registered but the matching
/// subscription interceptor was not wired, which happens when the scheme is registered after
/// <c>AddTraxGraphQL()</c>.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> chooses its socket-session interceptor from what the
/// <c>IServiceCollection</c> holds at the moment it runs, so a scheme registered after it is
/// invisible and no interceptor is wired. HotChocolate then falls back to
/// <see cref="DefaultSocketSessionInterceptor"/>, which accepts every <c>connection_init</c>:
/// subscriptions stop authenticating, silently, while HTTP keeps working because
/// <c>@authorize</c> is attached to the schema and does not depend on registration order.
/// <para>
/// This validator runs once the application container is complete, so it sees the schemes
/// regardless of order and can say plainly that the ordering is wrong.
/// </para>
/// </remarks>
internal sealed class TraxSubscriptionAuthWiringValidator(
    IServiceProviderIsService isService,
    IRequestExecutorProvider executorProvider,
    string schemaName,
    IReadOnlyCollection<string> wiredInterceptors
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A host is free to supply its own interceptor through ConfigureSchema, in which case
        // subscriptions are gated by that and Trax's wiring is irrelevant. Only the fall back to
        // HotChocolate's accept-everything default is a problem.
        var executor = await executorProvider
            .GetExecutorAsync(schemaName, cancellationToken)
            .ConfigureAwait(false);

        // Exact type, not a pattern match: every real interceptor (Trax's included) derives
        // from DefaultSocketSessionInterceptor, so `is not DefaultSocketSessionInterceptor`
        // would never be true.
        var active = executor.Schema.Services.GetService<ISocketSessionInterceptor>();
        if (active is not null && active.GetType() != typeof(DefaultSocketSessionInterceptor))
            return;

        var missing = new List<string>();

        // The dispatcher supersedes the single-scheme JWT interceptor, so either satisfies JWT.
        var jwtWired =
            wiredInterceptors.Contains(nameof(TraxJwtDispatcherSocketInterceptor))
            || wiredInterceptors.Contains(nameof(TraxJwtSocketInterceptor));

        if (isService.IsService(typeof(JwtDispatcherRuntime)) && !jwtWired)
            missing.Add("AddTraxJwtDispatcher()");
        else if (isService.IsService(typeof(ITraxPrincipalResolver<JwtTokenInput>)) && !jwtWired)
            missing.Add("AddTraxJwtAuth(...)");

        if (
            isService.IsService(typeof(ITraxPrincipalResolver<string>))
            && !wiredInterceptors.Contains(nameof(TraxApiKeySocketInterceptor))
        )
            missing.Add("AddTraxApiKeyAuth(...)");

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{string.Join(" and ", missing)} ran after AddTraxGraphQL(), so no subscription "
                + "interceptor was wired for it. WebSocket clients would connect without being "
                + "authenticated at all, because HotChocolate falls back to an interceptor that "
                + "accepts every connection_init. HTTP requests are unaffected, which is what "
                + "makes this easy to miss.\n"
                + "Move the authentication registration above AddTraxGraphQL():\n"
                + "  services.AddTraxJwtAuth(...);      // or AddTraxApiKeyAuth / AddTraxJwtDispatcher\n"
                + "  services.AddTraxGraphQL(...);"
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
