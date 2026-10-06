namespace Trax.Api.Auth.Jwt.Testing;

/// <summary>
/// Optional configuration for <see cref="TestJwksServer.StartAsync(TestJwksServerOptions?, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record TestJwksServerOptions
{
    /// <summary>
    /// Kestrel listen URL. Defaults to <c>http://127.0.0.1:0</c> (random
    /// loopback port). Use <c>http://+:8080</c> to bind a fixed port for a
    /// containerized local-cognito service.
    /// </summary>
    public string ListenUrl { get; init; } = "http://127.0.0.1:0";

    /// <summary>
    /// Override the <c>iss</c> claim and OIDC discovery <c>issuer</c> value.
    /// Defaults to the resolved bind URL (plus <see cref="PathPrefix"/>).
    /// Useful when the server is reverse-proxied or when matching a
    /// production Cognito issuer URL shape such as
    /// <c>https://cognito-idp.{region}.amazonaws.com/{userPoolId}</c>.
    /// </summary>
    public string? IssuerOverride { get; init; }

    /// <summary>
    /// Path prefix under which the JWKS and discovery endpoints are mounted.
    /// Defaults to the empty string (mounted at root). Set to a value like
    /// <c>/local_us-east-1_xxx</c> to mirror Cognito's pool-scoped URL
    /// layout. Must start with <c>/</c> if non-empty.
    /// </summary>
    public string PathPrefix { get; init; } = string.Empty;
}
