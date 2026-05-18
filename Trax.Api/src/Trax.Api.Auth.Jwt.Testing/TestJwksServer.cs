using System.Collections.Concurrent;
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
/// By default the server listens on a random loopback port and serves over
/// plain HTTP. Tests must call <c>AllowHttpMetadata()</c> on the
/// <see cref="JwtBuilder"/> so the bearer handler accepts the non-HTTPS
/// authority. Never reuse this server outside of tests.
/// </para>
/// <para>
/// Multi-key support: a server starts with one signing key.
/// <see cref="AddSigningKey(RSA?)"/> publishes additional keys for testing
/// JWKS rotation; <see cref="RemoveSigningKey(string)"/> removes them. The
/// "current" key (used by <see cref="SigningKey"/> and
/// <see cref="CreateIssuer(string)"/>) is the most-recently-added entry.
/// </para>
/// </remarks>
public sealed class TestJwksServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly ConcurrentDictionary<string, KeyEntry> _keys = new(StringComparer.Ordinal);
    private readonly object _currentKidLock = new();
    private string _currentKid;

    /// <summary>The authority URL (issuer) advertised by this JWKS server.</summary>
    public string Issuer { get; }

    /// <summary>Absolute URL of the JWKS endpoint.</summary>
    public string JwksUri { get; }

    /// <summary>
    /// Signing credentials for the "current" key (the most-recently-added
    /// entry, or the bootstrap key if no others have been added). Preserved
    /// for backward compatibility with single-key tests.
    /// </summary>
    public SigningCredentials SigningCredentials => GetCurrentEntry().Credentials;

    /// <summary>The RSA security key for the "current" key. See <see cref="SigningCredentials"/>.</summary>
    public RsaSecurityKey SigningKey => GetCurrentEntry().Key;

    /// <summary>List of every <c>kid</c> currently published in the JWKS document.</summary>
    public IReadOnlyList<string> SigningKeyIds => _keys.Keys.ToArray();

    /// <summary>
    /// Mint a token issuer pre-configured with this server's signing keys
    /// and issuer URL. The returned issuer signs with the current key by
    /// default; switch with <see cref="TestTokenIssuer.WithSigningKey"/>.
    /// </summary>
    public TestTokenIssuer CreateIssuer(string audience) =>
        new(Issuer, audience, SigningCredentials, BuildKeysetSnapshot());

    /// <summary>
    /// Snapshot of every published signing credential, keyed by <c>kid</c>.
    /// Used by the issuer to switch keys and by tests that need direct
    /// access to a particular key.
    /// </summary>
    public IReadOnlyDictionary<string, SigningCredentials> SigningCredentialsByKid =>
        _keys.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Credentials, StringComparer.Ordinal);

    private TestJwksServer(IHost host, string issuer, string jwksUri, KeyEntry bootstrap)
    {
        _host = host;
        Issuer = issuer;
        JwksUri = jwksUri;
        _keys[bootstrap.Kid] = bootstrap;
        _currentKid = bootstrap.Kid;
    }

    /// <summary>
    /// Start a new server on a random loopback port. Returns once Kestrel
    /// is ready to accept connections.
    /// </summary>
    public static Task<TestJwksServer> StartAsync(CancellationToken ct = default) =>
        StartAsync(options: null, ct);

    /// <summary>
    /// Start a new server with the supplied options. Returns once Kestrel
    /// is ready to accept connections.
    /// </summary>
    public static async Task<TestJwksServer> StartAsync(
        TestJwksServerOptions? options,
        CancellationToken ct = default
    )
    {
        options ??= new TestJwksServerOptions();
        ValidatePathPrefix(options.PathPrefix);

        var bootstrap = CreateKeyEntry(rsa: null);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(options.ListenUrl);
        builder.Services.AddSingleton<IHostLifetime, NoopLifetime>();
        builder.Logging.ClearProviders();

        var app = builder.Build();

        var serverRef = new ServerRef();
        var prefix = options.PathPrefix;

        app.MapGet(
            $"{prefix}/.well-known/openid-configuration",
            (HttpContext http) =>
            {
                var issuer = serverRef.Instance?.Issuer ?? ResolveIssuerFromRequest(http, prefix);
                var discovery = new
                {
                    issuer,
                    jwks_uri = $"{issuer}/.well-known/jwks.json",
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                };
                return Results.Json(discovery);
            }
        );

        app.MapGet(
            $"{prefix}/.well-known/jwks.json",
            (HttpContext http) =>
            {
                http.Response.ContentType = "application/json";
                var json = serverRef.Instance is { } s
                    ? s.BuildJwksJson()
                    : BuildSingleKeyJwksJson(bootstrap);
                return Results.Content(json, "application/json");
            }
        );

        await app.StartAsync(ct);

        var addresses = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;
        var authority = addresses.First().TrimEnd('/');
        var resolvedIssuer = options.IssuerOverride ?? $"{authority}{prefix}";
        var jwksUri = $"{resolvedIssuer}/.well-known/jwks.json";

        var server = new TestJwksServer(app, resolvedIssuer, jwksUri, bootstrap);
        serverRef.Instance = server;
        return server;
    }

    /// <summary>
    /// Add a new signing key to the published JWKS and make it the current
    /// key for new tokens. Returns the assigned <c>kid</c>.
    /// </summary>
    /// <param name="rsa">
    /// Optional pre-built RSA key. When null, a fresh 2048-bit key is
    /// generated. The server takes ownership and disposes it on
    /// <see cref="DisposeAsync"/>.
    /// </param>
    public string AddSigningKey(RSA? rsa = null)
    {
        var entry = CreateKeyEntry(rsa);
        _keys[entry.Kid] = entry;
        lock (_currentKidLock)
            _currentKid = entry.Kid;
        return entry.Kid;
    }

    /// <summary>
    /// Remove a signing key from the published JWKS. Tokens signed with
    /// this key fail validation once the validator refreshes its JWKS
    /// cache. Returns true if a key with this <c>kid</c> was removed.
    /// </summary>
    public bool RemoveSigningKey(string kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        if (!_keys.TryRemove(kid, out var removed))
            return false;

        removed.Dispose();

        lock (_currentKidLock)
        {
            if (string.Equals(_currentKid, kid, StringComparison.Ordinal))
            {
                // Promote any remaining key to current; if none left, leave
                // the field pointing at the removed kid so GetCurrentEntry
                // throws a clear error rather than silently switching.
                var remaining = _keys.Keys.FirstOrDefault();
                if (remaining is not null)
                    _currentKid = remaining;
            }
        }
        return true;
    }

    /// <summary>Stop the host and release every signing key's RSA handle.</summary>
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
        foreach (var entry in _keys.Values)
            entry.Dispose();
    }

    private KeyEntry GetCurrentEntry()
    {
        string kid;
        lock (_currentKidLock)
            kid = _currentKid;
        if (!_keys.TryGetValue(kid, out var entry))
            throw new InvalidOperationException(
                "TestJwksServer has no current signing key. The last key was removed without "
                    + "adding a replacement. Call AddSigningKey() before minting more tokens."
            );
        return entry;
    }

    private IReadOnlyDictionary<string, SigningCredentials> BuildKeysetSnapshot() =>
        _keys.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Credentials, StringComparer.Ordinal);

    private string BuildJwksJson()
    {
        var keys = _keys
            .Values.Select(k =>
            {
                var p = k.Rsa.ExportParameters(false);
                return new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = k.Kid,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(p.Modulus!),
                    e = Base64UrlEncoder.Encode(p.Exponent!),
                };
            })
            .ToArray();
        return JsonSerializer.Serialize(new { keys });
    }

    private static string BuildSingleKeyJwksJson(KeyEntry entry)
    {
        var p = entry.Rsa.ExportParameters(false);
        var key = new
        {
            kty = "RSA",
            use = "sig",
            kid = entry.Kid,
            alg = "RS256",
            n = Base64UrlEncoder.Encode(p.Modulus!),
            e = Base64UrlEncoder.Encode(p.Exponent!),
        };
        return JsonSerializer.Serialize(new { keys = new[] { key } });
    }

    private static KeyEntry CreateKeyEntry(RSA? rsa)
    {
        var key = rsa ?? RSA.Create(2048);
        var kid = "trax-test-" + Guid.NewGuid().ToString("N")[..8];
        var rsaSecurityKey = new RsaSecurityKey(key) { KeyId = kid };
        var credentials = new SigningCredentials(rsaSecurityKey, SecurityAlgorithms.RsaSha256);
        return new KeyEntry(kid, key, rsaSecurityKey, credentials, OwnsRsa: rsa is null);
    }

    private static string ResolveIssuerFromRequest(HttpContext http, string prefix) =>
        $"{http.Request.Scheme}://{http.Request.Host.Value}{prefix}";

    private static void ValidatePathPrefix(string prefix)
    {
        if (prefix.Length > 0 && !prefix.StartsWith('/'))
            throw new ArgumentException(
                "PathPrefix must start with '/' when non-empty.",
                nameof(prefix)
            );
        if (prefix.EndsWith('/'))
            throw new ArgumentException("PathPrefix must not end with '/'.", nameof(prefix));
    }

    private sealed record KeyEntry(
        string Kid,
        RSA Rsa,
        RsaSecurityKey Key,
        SigningCredentials Credentials,
        bool OwnsRsa
    ) : IDisposable
    {
        public void Dispose()
        {
            if (OwnsRsa)
                Rsa.Dispose();
        }
    }

    private sealed class ServerRef
    {
        public TestJwksServer? Instance { get; set; }
    }

    private sealed class NoopLifetime : IHostLifetime
    {
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
