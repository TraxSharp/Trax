namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Persistence contract for Cognito-style refresh tokens. Real Cognito
/// refresh tokens are opaque blobs; the format is not part of the OIDC
/// contract and downstream consumers (NextAuth, Amplify) treat them as
/// bytes. This interface captures the operations a Cognito-compatible
/// auth server must support: issuance, validation, rotation, and revocation
/// (single token or fan-out by user).
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Rotation chains: <see cref="RotateAsync"/> consumes one token and emits a
/// new one bound to the same chain. <see cref="RevokeAsync"/> revokes the
/// entire chain, matching Cognito's <c>RevokeToken</c> behavior where
/// revoking any token in a refresh-rotation chain revokes all of them.
/// </para>
/// <para>
/// Trax ships <see cref="InMemoryRefreshTokenStore"/> for tests and local
/// development. Production hosts implement this interface against their own
/// persistence (a relational table keyed by token hash, a Redis namespace
/// with per-key TTL, etc.).
/// </para>
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Issue a new refresh token bound to a <c>(sub, clientId)</c> pair.
    /// Starts a new rotation chain.
    /// </summary>
    Task<RefreshTokenHandle> IssueAsync(
        Guid sub,
        string clientId,
        TimeSpan lifetime,
        CancellationToken ct
    );

    /// <summary>
    /// Validate a token. Returns the bound <c>(sub, clientId)</c> if the
    /// token is active; null if expired, revoked, consumed by rotation, or
    /// unknown.
    /// </summary>
    Task<RefreshTokenClaims?> ValidateAsync(string token, CancellationToken ct);

    /// <summary>
    /// Validate the supplied token, mark it consumed, and issue a new token
    /// in the same rotation chain. Returns the new handle, or null if the
    /// supplied token was invalid (expired, revoked, already rotated, or
    /// unknown). The new token inherits the original token's expiry, not
    /// the original lifetime: rotation does not extend session length.
    /// </summary>
    Task<RefreshTokenHandle?> RotateAsync(string oldToken, CancellationToken ct);

    /// <summary>
    /// Revoke the supplied token. The whole rotation chain it belongs to is
    /// revoked so tokens already emitted from prior rotations also fail
    /// subsequent <see cref="ValidateAsync"/> calls.
    /// </summary>
    Task RevokeAsync(string token, CancellationToken ct);

    /// <summary>
    /// Revoke every refresh token (across all rotation chains) for a
    /// <c>(sub, clientId)</c> pair. Used for global sign-out and admin
    /// "revoke all sessions" operations.
    /// </summary>
    Task RevokeAllAsync(Guid sub, string clientId, CancellationToken ct);
}

/// <summary>
/// Handle returned by <see cref="IRefreshTokenStore.IssueAsync"/> and
/// <see cref="IRefreshTokenStore.RotateAsync"/>. The token is opaque to the
/// caller and only meaningful to the store that produced it.
/// </summary>
public sealed record RefreshTokenHandle(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Claims bound to a refresh token. Returned by
/// <see cref="IRefreshTokenStore.ValidateAsync"/> when the token is active.
/// </summary>
public sealed record RefreshTokenClaims(Guid Sub, string ClientId, DateTimeOffset ExpiresAt);
