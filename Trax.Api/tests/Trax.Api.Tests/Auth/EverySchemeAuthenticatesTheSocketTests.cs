using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Every JWT scheme registered with <c>AddTraxJwtAuth</c>, including named schemes with no
/// dispatcher routing between them, authenticates a subscription connection, and a connection
/// with no credential for any of them is rejected.
///
/// <para>Enforces <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md")]
[TestFixture]
public class EverySchemeAuthenticatesTheSocketTests
{
    private const string Adr = "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md";
    private const string Audience = "every-scheme";
    private const string CustomerIssuer = "https://customer.example";
    private const string StaffIssuer = "https://staff.example";
    private static readonly byte[] CustomerKey = Encoding.UTF8.GetBytes(new string('c', 32));
    private static readonly byte[] StaffKey = Encoding.UTF8.GetBytes(new string('s', 32));

    private static TraxCompositeSocketInterceptor NamedSchemesOnly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth(
            "Customer",
            jwt => jwt.UseSymmetricKey(CustomerIssuer, Audience, CustomerKey)
        );
        services.AddTraxJwtAuth(
            "Staff",
            jwt => jwt.UseSymmetricKey(StaffIssuer, Audience, StaffKey)
        );
        return new TraxCompositeSocketInterceptor(
            new TraxApplicationServices(services.BuildServiceProvider())
        );
    }

    private static string Sign(string issuer, byte[] key, string sub = "alice")
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                issuer,
                Audience,
                [new Claim("sub", sub)],
                now.AddMinutes(-1),
                now.AddMinutes(10),
                new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
            )
        );
    }

    [Test]
    public async Task NamedSchemesOnly_EmptyPayload_IsRejected()
    {
        var (session, _) = NewSession();

        var status = await NamedSchemesOnly().OnConnectAsync(session, Payload(new { }));

        status
            .Accepted.Should()
            .BeFalse("a host with only named JWT schemes still authenticates sockets, per " + Adr);
    }

    [Test]
    public async Task NamedSchemesOnly_TokenForTheFirstScheme_IsAccepted()
    {
        var (session, http) = NewSession();

        var status = await NamedSchemesOnly()
            .OnConnectAsync(
                session,
                Payload(new { authToken = Sign(CustomerIssuer, CustomerKey) })
            );

        status.Accepted.Should().BeTrue();
        http.User.Identity!.AuthenticationType.Should().Be("Customer");
    }

    [Test]
    public async Task NamedSchemesOnly_TokenForTheSecondScheme_IsAccepted()
    {
        var (session, http) = NewSession();

        var status = await NamedSchemesOnly()
            .OnConnectAsync(session, Payload(new { bearer = Sign(StaffIssuer, StaffKey) }));

        status.Accepted.Should().BeTrue();
        http.User.Identity!.AuthenticationType.Should().Be("Staff");
    }

    [Test]
    public async Task NamedSchemesOnly_TokenSignedWithTheWrongKey_IsRejected()
    {
        var (session, _) = NewSession();

        var status = await NamedSchemesOnly()
            .OnConnectAsync(session, Payload(new { authToken = Sign(CustomerIssuer, StaffKey) }));

        status.Accepted.Should().BeFalse();
    }
}
