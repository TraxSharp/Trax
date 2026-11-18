namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Handle returned by <see cref="IRefreshTokenStore.IssueAsync"/> and
/// <see cref="IRefreshTokenStore.RotateAsync"/>. The token is opaque to the
/// caller and only meaningful to the store that produced it.
/// </summary>
public sealed record RefreshTokenHandle(string Token, DateTimeOffset ExpiresAt);
