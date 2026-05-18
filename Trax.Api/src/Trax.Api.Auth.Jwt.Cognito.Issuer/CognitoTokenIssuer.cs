using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Mints Cognito-shaped JWTs. The symmetric counterpart to
/// <see cref="CognitoJwtPrincipalResolver"/>: tokens produced here round-trip
/// through that resolver to a <see cref="TraxPrincipal"/> with the same shape
/// a real Cognito-issued token would produce.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Access tokens differ from ID tokens in three claims that matter for
/// validation:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>token_use</c>: <c>access</c> for access tokens, <c>id</c> for ID tokens.
/// </item>
/// <item>
/// Audience: access tokens carry the app client id in <c>client_id</c> and
/// omit <c>aud</c> entirely; ID tokens carry it in <c>aud</c>. The
/// <c>UseCognito</c> audience validator accepts either shape based on the
/// configured <c>CognitoTokenUse</c>.
/// </item>
/// <item>
/// Profile claims: only ID tokens carry <c>email</c>, <c>given_name</c>,
/// <c>family_name</c>, and the <c>identities</c> federation array.
/// </item>
/// </list>
/// <para>
/// The signing <see cref="RsaSecurityKey"/> must have a <c>KeyId</c> set.
/// The same value is written to the JWT header <c>kid</c> and must be
/// discoverable in the JWKS the validator fetches. The
/// <c>TestJwksServer.CreateCognitoIssuer</c> extension wires this for tests.
/// </para>
/// </remarks>
public sealed class CognitoTokenIssuer
{
    private static readonly JsonWriterOptions JsonWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _issuer;
    private readonly SigningCredentials _signingCredentials;
    private readonly string _kid;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Construct an issuer that mints tokens against the supplied identity
    /// pool URL and signs with the supplied RSA key.
    /// </summary>
    /// <param name="issuer">
    /// The <c>iss</c> claim value. Real Cognito uses
    /// <c>https://cognito-idp.{region}.amazonaws.com/{userPoolId}</c>.
    /// Local-cognito services should match this shape using a configured
    /// base URL plus pool id.
    /// </param>
    /// <param name="signingKey">
    /// RSA key. Must have <see cref="SecurityKey.KeyId"/> set so the
    /// published JWKS entry and the token header <c>kid</c> align.
    /// </param>
    /// <param name="clock">
    /// Clock source. Inject a deterministic <see cref="TimeProvider"/> in
    /// tests; defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public CognitoTokenIssuer(string issuer, RsaSecurityKey signingKey, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentNullException.ThrowIfNull(signingKey);
        if (string.IsNullOrWhiteSpace(signingKey.KeyId))
            throw new ArgumentException(
                "CognitoTokenIssuer requires a signing key with a non-empty KeyId. "
                    + "Set RsaSecurityKey.KeyId so the JWKS entry and the token header `kid` align.",
                nameof(signingKey)
            );

        _issuer = issuer;
        _kid = signingKey.KeyId;
        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Mints a Cognito access token. Sets <c>token_use=access</c>,
    /// <c>client_id</c>, <c>iss</c>, <c>sub</c>, <c>auth_time</c>, <c>iat</c>,
    /// <c>exp</c>, <c>jti</c>, <c>scope</c>, <c>username</c>, and a repeated
    /// <c>cognito:groups</c> claim per group. Does not set <c>aud</c>: real
    /// Cognito access tokens carry the audience in <c>client_id</c> instead.
    /// </summary>
    public string MintAccessToken(CognitoAccessTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        if (request.Lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Lifetime must be positive.");

        var now = _clock.GetUtcNow();
        var authTime = request.AuthTime ?? now;
        var claims = new List<Claim>
        {
            new("sub", request.Sub.ToString()),
            new(CognitoDefaults.TokenUse, CognitoDefaults.TokenUseAccess),
            new(CognitoDefaults.ClientId, request.ClientId),
            new("auth_time", ToEpochSeconds(authTime).ToString(), ClaimValueTypes.Integer64),
            new("jti", Guid.NewGuid().ToString()),
            new(
                "username",
                string.IsNullOrEmpty(request.Username) ? request.Sub.ToString() : request.Username
            ),
        };

        if (request.Scopes.Count > 0)
            claims.Add(new Claim("scope", string.Join(' ', request.Scopes)));

        foreach (var group in request.Groups)
            claims.Add(new Claim(CognitoDefaults.CognitoGroups, group));

        AddAdditionalClaims(claims, request.AdditionalClaims);

        return WriteToken(claims, audience: null, now, now + request.Lifetime);
    }

    /// <summary>
    /// Mints a Cognito ID token. Sets <c>token_use=id</c>, <c>aud</c>
    /// (= clientId), <c>iss</c>, <c>sub</c>, <c>auth_time</c>, <c>iat</c>,
    /// <c>exp</c>, <c>jti</c>, <c>email</c>, <c>email_verified</c>,
    /// <c>given_name</c>, <c>family_name</c>, <c>cognito:username</c>,
    /// repeated <c>cognito:groups</c>, and the <c>identities</c> JSON
    /// claim for federated identities.
    /// </summary>
    public string MintIdToken(CognitoIdTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        if (request.Lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Lifetime must be positive.");

        var now = _clock.GetUtcNow();
        var authTime = request.AuthTime ?? now;
        var claims = new List<Claim>
        {
            new("sub", request.Sub.ToString()),
            new(CognitoDefaults.TokenUse, CognitoDefaults.TokenUseId),
            new("auth_time", ToEpochSeconds(authTime).ToString(), ClaimValueTypes.Integer64),
            new("jti", Guid.NewGuid().ToString()),
            new(CognitoDefaults.Email, request.Email),
            new(
                CognitoDefaults.EmailVerified,
                request.EmailVerified ? "true" : "false",
                ClaimValueTypes.Boolean
            ),
            new(
                CognitoDefaults.CognitoUsername,
                string.IsNullOrEmpty(request.Username) ? request.Sub.ToString() : request.Username
            ),
        };

        if (!string.IsNullOrEmpty(request.GivenName))
            claims.Add(new Claim("given_name", request.GivenName));
        if (!string.IsNullOrEmpty(request.FamilyName))
            claims.Add(new Claim("family_name", request.FamilyName));

        foreach (var group in request.Groups)
            claims.Add(new Claim(CognitoDefaults.CognitoGroups, group));

        if (request.Identities.Count > 0)
            claims.Add(
                new Claim(CognitoDefaults.Identities, SerializeIdentities(request.Identities))
            );

        AddAdditionalClaims(claims, request.AdditionalClaims);

        return WriteToken(claims, audience: request.ClientId, now, now + request.Lifetime);
    }

    private string WriteToken(
        IEnumerable<Claim> claims,
        string? audience,
        DateTimeOffset issuedAt,
        DateTimeOffset expires
    )
    {
        var iat = issuedAt.UtcDateTime;
        var exp = expires.UtcDateTime;

        var jwt = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims,
            notBefore: iat,
            expires: exp,
            signingCredentials: _signingCredentials
        );

        // Cognito tokens carry `iat`; JwtSecurityToken does not set it from
        // the constructor (only `nbf` and `exp`). Add it explicitly so the
        // claim shape matches.
        jwt.Header["kid"] = _kid;
        jwt.Payload["iat"] = ToEpochSeconds(issuedAt);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static void AddAdditionalClaims(
        List<Claim> claims,
        IReadOnlyDictionary<string, string>? additional
    )
    {
        if (additional is null)
            return;
        foreach (var (type, value) in additional)
        {
            if (string.IsNullOrWhiteSpace(type))
                continue;
            claims.Add(new Claim(type, value ?? string.Empty));
        }
    }

    private static long ToEpochSeconds(DateTimeOffset value) => value.ToUnixTimeSeconds();

    /// <summary>
    /// Serializes the identities list in real-Cognito shape: a JSON array of
    /// objects with camelCase keys. The claim value is written as a JSON
    /// array (Cognito does this), which <see cref="CognitoJwtPrincipalResolver"/>
    /// parses directly.
    /// </summary>
    private static string SerializeIdentities(IReadOnlyList<FederatedIdentity> identities)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, JsonWriterOptions))
        {
            writer.WriteStartArray();
            foreach (var identity in identities)
            {
                writer.WriteStartObject();
                writer.WriteString("userId", identity.UserId);
                writer.WriteString("providerName", identity.ProviderName);
                writer.WriteString("providerType", identity.ProviderType);
                writer.WriteBoolean("primary", identity.Primary);
                writer.WriteString(
                    "dateCreated",
                    identity.DateCreated.ToUnixTimeMilliseconds().ToString()
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
