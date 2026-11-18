namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Claims bound to a refresh token. Returned by
/// <see cref="IRefreshTokenStore.ValidateAsync"/> when the token is active.
/// </summary>
public sealed record RefreshTokenClaims(Guid Sub, string ClientId, DateTimeOffset ExpiresAt);
