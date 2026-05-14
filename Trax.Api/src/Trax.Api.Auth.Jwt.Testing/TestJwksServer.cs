using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt.Testing;

/// <summary>
/// In-process Kestrel host that serves an OIDC discovery document and a
/// JWKS endpoint suitable for the Trax JWT bearer middleware. Use in
/// integration tests to validate RS256-signed tokens end to end without
/// reaching out to a real identity provider.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// The server listens on a random loopback port and serves over plain HTTP.
/// Tests must call <c>AllowHttpMetadata()</c> on the <see cref="JwtBuilder"/>
/// so the bearer handler accepts the non-HTTPS authority. Never reuse this
/// server outside of tests.
/// </para>
/// </remarks>
public sealed class TestJwksServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly RSA _rsa;
    private readonly string _kid;

    /// <summary>The authority URL (issuer) of this JWKS server.</summary>
    public string Issuer { get; }

    /// <summary>Absolute URL of the JWKS endpoint.</summary>
    public string JwksUri => Issuer + "/.well-known/jwks.json";

    /// <summary>Signing credentials matching the published JWKS entry.</summary>
    public SigningCredentials SigningCredentials { get; }

    /// <summary>The RSA security key exposed via the JWKS endpoint.</summary>
    public RsaSecurityKey SigningKey { get; }

    /// <summary>
    /// Mint a token issuer pre-configured with this server's signing key and
    /// issuer URL.
    /// </summary>
    public TestTokenIssuer CreateIssuer(string audience) =>
        new(Issuer, audience, SigningCredentials);

    private TestJwksServer(IHost host, RSA rsa, RsaSecurityKey key, string kid, string issuer)
    {
        _host = host;
        _rsa = rsa;
        _kid = kid;
        Issuer = issuer;
        SigningKey = key;
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    /// <summary>
    /// Start a new server on a random loopback port. Returns once Kestrel
    /// is ready to accept connections.
    /// </summary>
    public static async Task<TestJwksServer> StartAsync(CancellationToken ct = default)
    {
        var rsa = RSA.Create(2048);
        var kid = "trax-test-" + Guid.NewGuid().ToString("N")[..8];
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        var jwksJson = BuildJwksJson(rsa, kid);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Bind explicitly to 127.0.0.1 with a dynamic port. Kestrel's
            // ListenLocalhost(0) overload rejects port 0; ListenAnyIP would
            // also work but advertises on every interface which is more
            // permissive than necessary for a test server.
            options.Listen(System.Net.IPAddress.Loopback, 0);
        });
        builder.Services.AddSingleton<IHostLifetime, NoopLifetime>();
        builder.Logging.ClearProviders();

        var app = builder.Build();

        app.MapGet(
            "/.well-known/openid-configuration",
            (HttpContext http) =>
            {
                var issuer = ResolveIssuerFromRequest(http);
                var discovery = new
                {
                    issuer,
                    jwks_uri = issuer + "/.well-known/jwks.json",
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                };
                return Results.Json(discovery);
            }
        );

        app.MapGet(
            "/.well-known/jwks.json",
            (HttpContext http) =>
            {
                http.Response.ContentType = "application/json";
                return Results.Content(jwksJson, "application/json");
            }
        );

        await app.StartAsync(ct);

        var addresses = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;
        var authority = addresses.First().TrimEnd('/');

        return new TestJwksServer(app, rsa, key, kid, authority);
    }

    /// <summary>
    /// Stop the host and release the RSA key.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            // Host already stopped.
        }
        _host.Dispose();
        _rsa.Dispose();
    }

    private static string ResolveIssuerFromRequest(HttpContext http) =>
        $"{http.Request.Scheme}://{http.Request.Host.Value}";

    private static string BuildJwksJson(RSA rsa, string kid)
    {
        var parameters = rsa.ExportParameters(false);
        var key = new
        {
            kty = "RSA",
            use = "sig",
            kid,
            alg = "RS256",
            n = Base64UrlEncoder.Encode(parameters.Modulus!),
            e = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        return JsonSerializer.Serialize(new { keys = new[] { key } });
    }

    private sealed class NoopLifetime : IHostLifetime
    {
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
