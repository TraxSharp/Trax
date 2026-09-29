using System.Security.Claims;
using FluentAssertions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// With a token scheme registered, a subscription connection needs a credential in
/// <c>connection_init</c> even when its upgrade request is already authenticated.
///
/// <para>Enforces <c>docs/adr/0008-a-token-scheme-requires-a-credential-on-every-socket.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0008-a-token-scheme-requires-a-credential-on-every-socket.md")]
[TestFixture]
public class AuthenticatedUpgradeRequiresCredentialTests
{
    private const string UpgradeAdr =
        "docs/adr/0008-a-token-scheme-requires-a-credential-on-every-socket.md";

    [TestCase(TraxCompositeSocketInterceptorTests.Schemes.ApiKey)]
    [TestCase(
        TraxCompositeSocketInterceptorTests.Schemes.ApiKey
            | TraxCompositeSocketInterceptorTests.Schemes.Jwt
    )]
    public async Task AuthenticatedUpgrade_WithEmptyPayload_IsRejectedWhenATokenSchemeIsRegistered(
        TraxCompositeSocketInterceptorTests.Schemes schemes
    )
    {
        // The upgrade request already carries a principal, as a session cookie would give it.
        // With a token scheme registered the connection still needs its own credential.
        var interceptor = TraxCompositeSocketInterceptorTests.Build(schemes);
        var (session, http) = NewSession();
        http.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "session-user")], "Cookies")
        );

        var status = await interceptor.OnConnectAsync(session, Payload(new { }));

        status
            .Accepted.Should()
            .BeFalse(
                "an authenticated upgrade does not stand in for a connection_init credential, per "
                    + UpgradeAdr
            );
    }

    [Test]
    public async Task AuthenticatedUpgrade_WithAValidApiKey_IsAccepted()
    {
        // Control for the case above: the same session is accepted once it presents a key.
        var interceptor = TraxCompositeSocketInterceptorTests.Build(
            TraxCompositeSocketInterceptorTests.Schemes.ApiKey
        );
        var (session, http) = NewSession();
        http.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "session-user")], "Cookies")
        );

        var status = await interceptor.OnConnectAsync(
            session,
            Payload(new { apiKey = TraxCompositeSocketInterceptorTests.ApiKey })
        );

        status.Accepted.Should().BeTrue();
    }
}
