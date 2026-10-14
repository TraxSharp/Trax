---
title: IRefreshTokenStore
description: Reference for IRefreshTokenStore, the Cognito-style refresh token contract with rotation chains and reuse detection, and InMemoryRefreshTokenStore.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 10
package: Trax.Api.Auth.Jwt.Cognito.Issuer
namespace: Trax.Api.Auth.Jwt.Cognito.Issuer
source:
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/IRefreshTokenStore.cs
  - Trax.Api/src/Trax.Api.Auth.Jwt.Cognito.Issuer/InMemoryRefreshTokenStore.cs
---

# IRefreshTokenStore

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The operations a Cognito-compatible auth server needs for opaque refresh tokens: issue, validate, rotate and revoke. Tokens belong to rotation chains bound to a `(sub, clientId)` pair. `InMemoryRefreshTokenStore` implements it in process memory.

## Signature

```csharp fragment
public interface IRefreshTokenStore
{
    Task<RefreshTokenHandle> IssueAsync(Guid sub, string clientId, TimeSpan lifetime, CancellationToken ct);
    Task<RefreshTokenClaims?> ValidateAsync(string token, CancellationToken ct);
    Task<RefreshTokenHandle?> RotateAsync(string oldToken, CancellationToken ct);
    Task RevokeAsync(string token, CancellationToken ct);
    Task RevokeAllAsync(Guid sub, string clientId, CancellationToken ct);
}

public sealed record RefreshTokenHandle(string Token, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenClaims(Guid Sub, string ClientId, DateTimeOffset ExpiresAt);

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    public InMemoryRefreshTokenStore(TimeProvider? clock = null);
}
```

## Members

| Member | Description |
|---|---|
| `IssueAsync(sub, clientId, lifetime, ct)` | Starts a new rotation chain and returns its first token. |
| `ValidateAsync(token, ct)` | The token's `RefreshTokenClaims` when it is active; `null` when it is expired, revoked, consumed by rotation, or unknown. |
| `RotateAsync(oldToken, ct)` | Consumes `oldToken` and returns a new token in the same chain, with the chain's original expiry. `null` when `oldToken` is not active. |
| `RevokeAsync(token, ct)` | Revokes the whole chain the token belongs to. |
| `RevokeAllAsync(sub, clientId, ct)` | Revokes every chain of the pair: global sign-out. |
| `RefreshTokenHandle.Token`, `.ExpiresAt` | The opaque token and when its chain expires. |
| `RefreshTokenClaims.Sub`, `.ClientId`, `.ExpiresAt` | What the token is bound to, and when it expires. |

## Returns

See each member above.

## Behavior

- Rotation does not extend a session: every token in a chain expires when the first one does.
- Presenting a token that rotation already consumed is reuse. `RotateAsync` revokes the whole chain and returns `null`, so the token the earlier rotation issued stops working too: the store cannot tell the client from someone replaying a copy, so the session ends for both. An implementation of the interface must do the same.
- Concurrent rotations of one token: one caller wins, every other gets `null`. The losers presented a spent token, so they count as reuse and the chain, the winner's new token included, is revoked.
- `InMemoryRefreshTokenStore` issues 256-bit random tokens, holds them in process memory, and loses them on restart.

## Example

```csharp compile
var store = new InMemoryRefreshTokenStore();
var userId = Guid.NewGuid();

var refresh = await store.IssueAsync(userId, "mobile-app-client-id", TimeSpan.FromDays(30), ct);

var next = await store.RotateAsync(refresh.Token, ct);
if (next is null)
{
    // Rotated already, revoked or expired: the client signs in again.
}
```

## See also

- [CognitoTokenIssuer](/docs/sdk-reference/api-auth/cognito-token-issuer)
