using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxApiKeySocketInterceptorTests
{
    private static TraxApiKeySocketInterceptor NewInterceptor(
        ITraxPrincipalResolver<string> resolver
    ) => new(resolver, NullLogger<TraxApiKeySocketInterceptor>.Instance);

    private static ITraxPrincipalResolver<string> ResolverReturning(TraxPrincipal? principal)
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<string>>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(principal));
        return resolver;
    }

    [Test]
    public async Task EmptyPayload_Rejects()
    {
        var interceptor = NewInterceptor(ResolverReturning(null));
        var (session, _) = NewSession();

        var result = await interceptor.OnConnectAsync(
            session,
            EmptyPayload(),
            CancellationToken.None
        );

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Missing auth token");
    }

    [Test]
    public async Task BothKeysNull_Rejects()
    {
        var interceptor = NewInterceptor(ResolverReturning(null));
        var (session, _) = NewSession();

        var payload = Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload(null, null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Missing auth token");
    }

    [Test]
    public async Task WhitespaceKey_Rejects()
    {
        var interceptor = NewInterceptor(ResolverReturning(null));
        var (session, _) = NewSession();

        var payload = Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload("   ", null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
    }

    [Test]
    public async Task ResolverReturnsNull_Rejects()
    {
        var interceptor = NewInterceptor(ResolverReturning(null));
        var (session, _) = NewSession();

        var payload = Payload(
            new TraxApiKeySocketInterceptor.ConnectionInitPayload("bogus-key", null)
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("Invalid auth token");
    }

    [Test]
    public async Task ResolverThrows_Rejects_DoesNotBubble()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<string>>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<TraxPrincipal?>>(_ =>
                throw new InvalidOperationException("db down")
            );

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        var payload = Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload("k", null));
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Message.Should().Contain("resolver failed");
    }

    [Test]
    public async Task ValidKey_AuthTokenField_Accepts_AttachesPrincipal()
    {
        var resolver = ResolverReturning(
            new TraxPrincipal("alice", "Alice", ["Player"], PrincipalType: "apikey")
        );
        var interceptor = NewInterceptor(resolver);
        var (session, http) = NewSession();

        var payload = Payload(
            new TraxApiKeySocketInterceptor.ConnectionInitPayload("player-key", null)
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeTrue();
        http.User.Identity!.IsAuthenticated.Should().BeTrue();
        http.User.Identity.AuthenticationType.Should().Be("TraxApiKey");
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
        http.User.IsInRole("Player").Should().BeTrue();
    }

    [Test]
    public async Task ValidKey_ApiKeyField_AlsoAccepts()
    {
        // The interceptor accepts either 'authToken' (graphql-transport-ws
        // convention) or 'apiKey' (mirrors the REST header name).
        var resolver = ResolverReturning(new TraxPrincipal("alice", "Alice", []));
        var interceptor = NewInterceptor(resolver);
        var (session, http) = NewSession();

        var payload = Payload(
            new TraxApiKeySocketInterceptor.ConnectionInitPayload(
                AuthToken: null,
                ApiKey: "player-key"
            )
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeTrue();
        http.User.FindFirst(TraxAuthClaimTypes.PrincipalId)!.Value.Should().Be("alice");
    }

    [Test]
    public async Task AuthTokenTakesPrecedenceOverApiKeyField()
    {
        // When both fields are present authToken wins so the shape is
        // predictable. We verify by sending an authToken that resolves and
        // an apiKey that would not.
        var resolver = Substitute.For<ITraxPrincipalResolver<string>>();
        resolver
            .ResolveAsync("winning-token", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", [])));
        resolver
            .ResolveAsync("losing-key", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>((TraxPrincipal?)null));

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        var payload = Payload(
            new TraxApiKeySocketInterceptor.ConnectionInitPayload("winning-token", "losing-key")
        );
        var result = await interceptor.OnConnectAsync(session, payload, CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await resolver.Received(1).ResolveAsync("winning-token", Arg.Any<CancellationToken>());
        await resolver.DidNotReceive().ResolveAsync("losing-key", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancellationTokenFlowsToResolver()
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<string>>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", [])));

        var interceptor = NewInterceptor(resolver);
        var (session, _) = NewSession();

        using var cts = new CancellationTokenSource();
        var payload = Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload("k", null));
        await interceptor.OnConnectAsync(session, payload, cts.Token);

        await resolver.Received(1).ResolveAsync("k", cts.Token);
    }
}
