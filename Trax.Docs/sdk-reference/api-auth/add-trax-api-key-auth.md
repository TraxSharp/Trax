---
layout: default
title: AddTraxApiKeyAuth
parent: API Auth
grand_parent: SDK Reference
---

# AddTraxApiKeyAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Registers the Trax API-key authentication scheme, its authorization policy (`ApiKeyDefaults.PolicyName`), the combined `TraxAuthPolicy`, `IHttpContextAccessor`, and a one-shot startup disclaimer log.

## Signatures

```csharp
public static AuthenticationBuilder AddTraxApiKeyAuth(
    this IServiceCollection services,
    Action<ApiKeyBuilder> configure,
    Action<ApiKeyAuthenticationOptions>? configureOptions = null);

public static AuthenticationBuilder AddTraxApiKeyAuth<TResolver>(
    this IServiceCollection services,
    Action<ApiKeyAuthenticationOptions>? configureOptions = null)
    where TResolver : class, ITraxPrincipalResolver<string>;
```

Two paths, picked by shape of the credential source:

| Overload | Use when | Resolver lifetime |
|---|---|---|
| `AddTraxApiKeyAuth(Action<ApiKeyBuilder>)` | Keys are a static set known at startup (config, secret manager, constants). | Singleton `HashedApiKeyResolver` built from the configured entries. |
| `AddTraxApiKeyAuth<TResolver>()` | Keys come from a runtime source that needs scoped DI dependencies (DbContext, distributed cache, HTTP client). | Scoped, resolved from DI per request. |

## Static key set (builder overload)

Keys registered through the builder are salted and SHA-256 hashed at startup and compared with `CryptographicOperations.FixedTimeEquals` on every request. Cleartext comparison is not reachable from consumer code.

```csharp
services.AddTraxApiKeyAuth(keys => keys
    .Add("admin-key",  id: "admin",  "Admin", "Player")
    .Add("player-key", id: "player", "Player"));
```

When the principal needs a display name distinct from its id, or custom claims, use the factory overload of `Add`:

```csharp
services.AddTraxApiKeyAuth(keys => keys
    .Add("alice-key", () => new TraxPrincipal("alice", "Alice Liddell", ["User"])));
```

For production hosts that load salt and hash bytes from a secret manager (cleartext never enters the process), use `AddHashed`:

```csharp
var admin = builder.Configuration.GetSection("ApiKeys:Admin");

services.AddTraxApiKeyAuth(keys => keys.AddHashed(
    salt:   Convert.FromBase64String(admin["Salt"]!),
    sha256: Convert.FromBase64String(admin["Hash"]!),
    id:     "admin",
    "Admin"));
```

## DI-scoped resolver (generic overload)

For keys backed by a database, cache, or HTTP service, implement `ITraxPrincipalResolver<string>`:

```csharp
public sealed class MyApiKeyResolver(ApplicationDbContext db) : ITraxPrincipalResolver<string>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(string apiKey, CancellationToken ct)
    {
        var user = await db.ApiKeys.FirstOrDefaultAsync(k => k.Hash == Hash(apiKey), ct);
        return user is null
            ? null
            : new TraxPrincipal(user.Id, user.DisplayName, user.Roles);
    }
}

services.AddTraxApiKeyAuth<MyApiKeyResolver>();
```

The resolver is resolved per request, so scoped dependencies work as expected. Hash comparisons are the consumer's responsibility on this path; use `CryptographicOperations.FixedTimeEquals` against a stored hash, never a cleartext `==`.

## `ApiKeyBuilder` methods

| Method | Purpose |
|---|---|
| `Add(string key, string id, params string[] roles)` | Cleartext key; principal id doubles as display name. Covers the common case. |
| `Add(string key, Func<TraxPrincipal> principalFactory)` | Cleartext key with full principal control (distinct display name, custom claims). |
| `AddHashed(byte[] salt, byte[] sha256, string id, params string[] roles)` | Pre-hashed key; cleartext never enters the process. |
| `AddHashed(byte[] salt, byte[] sha256, Func<TraxPrincipal> principalFactory)` | Pre-hashed key with full principal control. |

`Build()` is internal. The extension method calls it and throws `InvalidOperationException` if no keys were registered.

## Return Semantics

| Header state | Result |
|---|---|
| Absent | `AuthenticateResult.NoResult()` (permits `[AllowAnonymous]`) |
| Present more than once | `AuthenticateResult.Fail("Multiple API keys presented.")`. The resolver is not invoked. |
| Present, resolver returns `null` | `AuthenticateResult.Fail(...)` |
| Present, resolver throws | `AuthenticateResult.Fail(...)` (logged at `Warning`, not `Error`) |
| Present, resolver returns `TraxPrincipal` | `AuthenticateResult.Success(ticket)` |

## Protecting Endpoints

`AddTraxApiKeyAuth` does not set a default authentication scheme. ASP.NET Core will not invoke the API-key handler for a plain `[Authorize]` attribute or `.RequireAuthorization()` with no argument. Name the scheme or policy explicitly:

```csharp
// Single-scheme case: use the policy registered by AddTraxApiKeyAuth
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(ApiKeyDefaults.PolicyName));

// Mixed-scheme case (API key OR JWT OR...): use the combined Trax policy
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy));
```

`TraxAuthClaimTypes.TraxAuthPolicy` is the name every Trax auth package registers into. Each `AddTrax*Auth` call adds its scheme to that policy's allowed-schemes list, so a route protected by `TraxAuthPolicy` accepts credentials from any configured Trax scheme.

If you want API-key auth to act as the default for the whole app, pass the scheme name to `AddAuthentication` directly:

```csharp
services.AddAuthentication(ApiKeyDefaults.SchemeName);
services.AddTraxApiKeyAuth(keys => keys.Add("...", id: "..."));
```

That is an application-level choice, not one Trax makes for you.
