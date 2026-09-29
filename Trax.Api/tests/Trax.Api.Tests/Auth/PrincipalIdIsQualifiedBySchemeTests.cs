using System.Text;
using FluentAssertions;
using HotChocolate.AspNetCore.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A principal id is qualified by the scheme that authenticated it, <c>{scheme}:{id}</c>, so the
/// same subject from two issuers is two principals, on HTTP and on sockets alike.
///
/// <para>Enforces <c>docs/adr/0023-a-principal-id-is-qualified-by-its-scheme.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0023-a-principal-id-is-qualified-by-its-scheme.md")]
[TestFixture]
public class PrincipalIdIsQualifiedBySchemeTests
{
    private const string Adr = "docs/adr/0023-a-principal-id-is-qualified-by-its-scheme.md";
    private const string Audience = "qualified-ids";
    private const string CustomerIssuer = "https://customers";
    private const string StaffIssuer = "https://staff";
    private static readonly byte[] CustomerKey = Encoding.UTF8.GetBytes(new string('c', 32));
    private static readonly byte[] StaffKey = Encoding.UTF8.GetBytes(new string('t', 32));

    private static ServiceProvider TwoIssuers()
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
        services.AddTraxJwtDispatcher(d =>
            d.MapIssuer(CustomerIssuer, "Customer").MapIssuer(StaffIssuer, "Staff")
        );
        return services.BuildServiceProvider();
    }

    private static string Mint(string issuer, byte[] key, string sub) =>
        TestTokenIssuer.Symmetric(issuer, Audience, key).Mint(b => b.WithSubject(sub));

    private static async Task<string?> HttpPrincipalId(
        IServiceProvider services,
        string scheme,
        string header,
        string value
    )
    {
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers[header] = value;
        var result = await scope
            .ServiceProvider.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, scheme);
        result.Succeeded.Should().BeTrue(result.Failure?.Message);
        return result.Principal!.TryGetPrincipalId(out var id) ? id : null;
    }

    private static async Task<string?> SocketPrincipalId(IServiceProvider services, object payload)
    {
        var interceptor = new TraxCompositeSocketInterceptor(new TraxApplicationServices(services));
        var (session, http) = NewSession();
        var status = await interceptor.OnConnectAsync(
            session,
            Payload(payload),
            CancellationToken.None
        );
        status.Accepted.Should().BeTrue(status.Message);
        return http.User.TryGetPrincipalId(out var id) ? id : null;
    }

    [Test]
    public async Task The_same_sub_from_two_schemes_yields_distinct_principals_over_http()
    {
        await using var sp = TwoIssuers();

        var customer = await HttpPrincipalId(
            sp,
            JwtDefaults.DispatcherSchemeName,
            "Authorization",
            "Bearer " + Mint(CustomerIssuer, CustomerKey, "abc")
        );
        var staff = await HttpPrincipalId(
            sp,
            JwtDefaults.DispatcherSchemeName,
            "Authorization",
            "Bearer " + Mint(StaffIssuer, StaffKey, "abc")
        );

        customer.Should().Be("Customer:abc", "the id is qualified by its scheme, per " + Adr);
        staff.Should().Be("Staff:abc", "the id is qualified by its scheme, per " + Adr);
    }

    [Test]
    public async Task The_same_sub_from_two_schemes_yields_distinct_principals_on_a_socket()
    {
        await using var sp = TwoIssuers();

        var customer = await SocketPrincipalId(
            sp,
            new { authToken = Mint(CustomerIssuer, CustomerKey, "abc") }
        );
        var staff = await SocketPrincipalId(
            sp,
            new { authToken = Mint(StaffIssuer, StaffKey, "abc") }
        );

        customer.Should().Be("Customer:abc", "a socket qualifies the id as HTTP does, per " + Adr);
        staff.Should().Be("Staff:abc", "a socket qualifies the id as HTTP does, per " + Adr);
    }

    [Test]
    public async Task An_api_key_principal_has_the_same_qualified_id_on_http_and_a_socket()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxApiKeyAuth(keys => keys.Add("the-key", id: "reporter"));
        await using var sp = services.BuildServiceProvider();

        var http = await HttpPrincipalId(
            sp,
            ApiKeyDefaults.SchemeName,
            ApiKeyDefaults.HeaderName,
            "the-key"
        );
        var socket = await SocketPrincipalId(sp, new { apiKey = "the-key" });

        http.Should().Be("TraxApiKey:reporter", "the id is qualified by its scheme, per " + Adr);
        socket.Should().Be(http, "both transports produce one id, per " + Adr);
    }

    [Test]
    public void ToClaimsPrincipal_qualifies_the_id_and_TryGetTraxPrincipal_returns_it()
    {
        var claims = new TraxPrincipal("abc", "Abc", []).ToClaimsPrincipal("Customer");

        claims.TryGetTraxPrincipal(out var principal).Should().BeTrue();
        principal!.Id.Should().Be("Customer:abc", "the claim carries the qualified id, per " + Adr);
        TraxPrincipalId.Qualify("Customer", "abc").Should().Be("Customer:abc");
    }

    [Test]
    public void An_authentication_type_containing_the_separator_is_refused()
    {
        // "A:B" + "c" and "A" + "B:c" would both read "A:B:c".
        var act = () => new TraxPrincipal("c", "C", []).ToClaimsPrincipal("A:B");

        act.Should()
            .Throw<ArgumentException>("a scheme name with ':' makes ids ambiguous, per " + Adr);
    }
}
