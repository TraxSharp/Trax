using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A socket session interceptor is a singleton built from the schema container, and every
/// dependency it declares is bridged out of the <b>root</b> application provider. A scoped
/// service cannot cross that bridge, so the interceptors take the container and open a scope
/// per connection instead.
/// </summary>
/// <remarks>
/// The constructors have declared <c>ITraxPrincipalResolver&lt;T&gt;</c> since the schemes were
/// written, but nothing bridged it: under HotChocolate 15 the wiring was a bare
/// <c>AddSocketSessionInterceptor</c>. The 15 to 16 upgrade (a4a22d9, first released in v1.41.0)
/// added an explicit <c>BridgeApplicationService</c> for the resolver, and a bridge reads the root
/// provider. From v1.41.0 on, a host that enabled subscriptions with scope validation on (the
/// default in Development and in WebApplicationFactory) failed at startup with
/// "Cannot resolve scoped service 'ITraxPrincipalResolver`1[System.String]' from root provider."
/// </remarks>
[TestFixture]
public class SocketInterceptorScopeTests
{
    private sealed class StubResolver : ITraxPrincipalResolver<string>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct) =>
            ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal(input, "Stub", ["role"]));
    }

    /// <summary>
    /// The application container as a host builds it: the resolver scoped, scope validation on.
    /// </summary>
    private static ServiceProvider NewAppProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxApiKeyAuth<StubResolver>();
        services.AddSingleton(sp => new TraxApplicationServices(sp));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// Pins the constraint the interceptors are written around. If this ever stops throwing, the
    /// resolver was quietly relaxed to a singleton and a host whose resolver holds a DbContext is
    /// sharing one across every connection.
    /// </summary>
    [Test]
    public void TheResolverCannotBeResolvedFromTheRootProvider()
    {
        using var provider = NewAppProvider();

        var act = () => provider.GetRequiredService<ITraxPrincipalResolver<string>>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*scoped service*from root provider*");
    }

    /// <summary>
    /// Mirrors how the schema container builds the interceptor: each constructor parameter comes
    /// out of the root application provider. A scoped parameter throws here.
    /// </summary>
    [Test]
    public void ApiKeyInterceptor_ConstructsFromTheRootProvider()
    {
        using var provider = NewAppProvider();

        var act = () => ActivatorUtilities.CreateInstance<TraxApiKeySocketInterceptor>(provider);

        act.Should().NotThrow();
    }

    [Test]
    public void JwtInterceptor_ConstructsFromTheRootProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey("iss", "aud", new byte[32]));
        services.AddSingleton(sp => new TraxApplicationServices(sp));
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );

        var act = () => ActivatorUtilities.CreateInstance<TraxJwtSocketInterceptor>(provider);

        act.Should().NotThrow();
    }

    /// <summary>
    /// The scope is real: the interceptor resolves the scoped resolver per connection and the
    /// principal it returns reaches HttpContext.
    /// </summary>
    [Test]
    public async Task ApiKeyInterceptor_ResolvesTheScopedResolverPerConnection()
    {
        using var provider = NewAppProvider();
        var interceptor = new TraxApiKeySocketInterceptor(
            new TraxApplicationServices(provider),
            provider.GetRequiredService<ILogger<TraxApiKeySocketInterceptor>>()
        );
        var (session, http) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload(null, "the-key")),
            CancellationToken.None
        );

        result.Accepted.Should().BeTrue();
        http.User.Identity!.IsAuthenticated.Should().BeTrue();
    }

    /// <summary>
    /// Two connections must not share a resolver instance. Scoped means scoped.
    /// </summary>
    [Test]
    public async Task EachConnectionGetsItsOwnResolverInstance()
    {
        var seen = new List<CountingResolver>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITraxPrincipalResolver<string>>(_ =>
        {
            var resolver = new CountingResolver();
            lock (seen)
                seen.Add(resolver);
            return resolver;
        });
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );

        var interceptor = new TraxApiKeySocketInterceptor(
            new TraxApplicationServices(provider),
            provider.GetRequiredService<ILogger<TraxApiKeySocketInterceptor>>()
        );

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var (session, _) = NewSession();
            await interceptor.OnConnectAsync(
                session,
                Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload(null, "the-key")),
                CancellationToken.None
            );
        }

        seen.Should().HaveCount(3, "each connection opens its own scope");
        seen.Should().OnlyContain(r => r.Calls == 1);
    }

    /// <summary>
    /// The per-connection scope is disposed when the connection attempt ends, whether it was
    /// accepted or rejected — otherwise a host whose resolver holds a DbConnection leaks one
    /// per connection.
    /// </summary>
    [Test]
    public async Task TheConnectionScopeIsDisposed()
    {
        DisposeTrackingResolver? created = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITraxPrincipalResolver<string>>(_ => created = new());
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );

        var interceptor = new TraxApiKeySocketInterceptor(
            new TraxApplicationServices(provider),
            provider.GetRequiredService<ILogger<TraxApiKeySocketInterceptor>>()
        );
        var (session, _) = NewSession();

        await interceptor.OnConnectAsync(
            session,
            Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload(null, "the-key")),
            CancellationToken.None
        );

        created!.Disposed.Should().BeTrue();
    }

    /// <summary>
    /// A resolver that throws must not escape as an unhandled exception on the socket: the
    /// connection is rejected. The scope still has to be disposed on that path.
    /// </summary>
    [Test]
    public async Task AThrowingResolverRejectsTheConnectionAndStillDisposesTheScope()
    {
        DisposeTrackingResolver? created = null;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITraxPrincipalResolver<string>>(_ => created = new() { Throw = true });
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );

        var interceptor = new TraxApiKeySocketInterceptor(
            new TraxApplicationServices(provider),
            provider.GetRequiredService<ILogger<TraxApiKeySocketInterceptor>>()
        );
        var (session, _) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload(null, "the-key")),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        created!.Disposed.Should().BeTrue();
    }

    private sealed class CountingResolver : ITraxPrincipalResolver<string>
    {
        public int Calls { get; private set; }

        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal(input, "Stub", ["role"]));
        }
    }

    private sealed class DisposeTrackingResolver : ITraxPrincipalResolver<string>, IDisposable
    {
        public bool Disposed { get; private set; }
        public bool Throw { get; init; }

        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct) =>
            Throw
                ? throw new InvalidOperationException("resolver blew up")
                : ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal(input, "Stub", ["role"]));

        public void Dispose() => Disposed = true;
    }
}
