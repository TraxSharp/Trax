---
layout: default
title: AddTraxApiKeyAuth
description: "Reference for AddTraxApiKeyAuth: static key sets and DI-scoped resolvers, ApiKeyBuilder methods, hashed key comparison, demo keys and protecting endpoints."
parent: API Auth
grand_parent: SDK Reference
---

# AddTraxApiKeyAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Registers the Trax API-key authentication scheme, its authorization policy (`ApiKeyDefaults.PolicyName`), the combined `TraxAuthPolicy`, the ASP.NET Core authentication services (`AddAuthentication()`), the injectable [`TraxPrincipal`](/docs/sdk-reference/api-auth/injecting-trax-principal) (`AddTraxPrincipalAccessor()`), `IHttpContextAccessor`, and a one-shot startup disclaimer log.

A principal this scheme authenticates carries the id `TraxApiKey:{id}`: the id you registered, qualified by the scheme name. See [Qualified Principal Ids](/docs/migration-guides/qualified-principal-ids).

If you call it conditionally (demo keys in Development, configured keys elsewhere, nothing when nothing is configured), also call `services.AddAuthentication()` and `services.AddTraxPrincipalAccessor()` unconditionally. Without the first, `app.UseAuthentication()` throws `Unable to resolve service for type 'Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider'` on a host where no `AddTrax*Auth` ran; without the second, the mediator's startup check refuses a host whose junctions inject `TraxPrincipal`. Both calls are idempotent. The [Auth sample](/docs/samples/auth) has the full shape.

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
if (builder.Environment.IsDevelopment())
{
    services.AddTraxApiKeyAuth(keys => keys
        .Add("admin-key-do-not-use-in-production",  id: "admin",  "Admin", "Player")
        .Add("player-key-do-not-use-in-production", id: "player", "Player")
        .Add("reader-key-do-not-use-in-production", id: "reader"));
}
```

The roles after `id` are optional. A key with none (`reader` above) authenticates as a caller with
no roles: it passes a bare `[TraxAuthorize]` and an authenticated query model, and is refused
(`TRAX_AUTHORIZATION`) by anything that names a role.

A key written into source is a demo key: give it the `do-not-use-in-production` marker and
register it only in Development, as above (see [Demo keys start only in Development](/docs/sdk-reference/api-auth/add-trax-api-key-auth#demo-keys-start-only-in-development)).
Real keys come from a secret store, through `AddHashed` or a resolver.

When the principal needs a display name distinct from its id, or custom claims, use the factory overload of `Add`:

```csharp
services.AddTraxApiKeyAuth(keys => keys
    .Add("alice-key-do-not-use-in-production",
        () => new TraxPrincipal("alice", "Alice Liddell", ["User"])));
```

### Pre-hashed keys

For production hosts that load salt and hash bytes from a secret manager (cleartext never enters the process), use `AddHashed`:

```csharp
var admin = builder.Configuration.GetSection("ApiKeys:Admin");

services.AddTraxApiKeyAuth(keys => keys.AddHashed(
    salt:   Convert.FromBase64String(admin["Salt"]!),
    sha256: Convert.FromBase64String(admin["Hash"]!),
    id:     "admin",
    "Admin"));
```

`sha256` is the SHA-256 of the salt bytes followed by the key's UTF-8 bytes: `SHA-256(salt || UTF-8(key))`. The salt must be non-empty; 16 random bytes is what `Add` uses. Produce the pair once, when you issue the key, and store both in the secret manager:

```csharp
using System.Security.Cryptography;
using System.Text;

var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // give this to the caller
var salt = RandomNumberGenerator.GetBytes(16);
var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(key)]);

Console.WriteLine($"Salt: {Convert.ToBase64String(salt)}");
Console.WriteLine($"Hash: {Convert.ToBase64String(hash)}");
```

`AddHashed(salt, sha256, id, roles)` builds the same principal as `Add(key, id, roles)`.

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
| `Add(string key, string id, params string[] roles)` | Cleartext key; principal id doubles as display name, `PrincipalType` is `apikey`. Covers the common case. |
| `Add(string key, Func<TraxPrincipal> principalFactory)` | Cleartext key with full principal control (distinct display name, custom claims). The factory builds the whole principal, so set `PrincipalType: "apikey"` yourself if anything reads it (the audit trail records it). |
| `AddHashed(byte[] salt, byte[] sha256, string id, params string[] roles)` | Pre-hashed key; cleartext never enters the process. `PrincipalType` is `apikey`. |
| `AddHashed(byte[] salt, byte[] sha256, Func<TraxPrincipal> principalFactory)` | Pre-hashed key with full principal control. As with the factory `Add`, you set `PrincipalType`. |

`Build()` is internal. The extension method calls it and throws `InvalidOperationException` if no keys were registered.

### Demo keys start only in Development

A cleartext key containing `ApiKeyBuilder.DemoKeyMarker` (`do-not-use-in-production`, compared
ignoring case) marks a published demo key, the kind the Trax templates and samples ship. When
one is registered through `Add`, the host refuses to start in any environment other than
Development, with a message naming the marker and the environment (never the key):

```
AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production', which marks a
published demo key, and the environment is 'Production'. Such keys start only in Development.
Register real keys (keys.AddHashed(...) from a secret store, or a resolver via
AddTraxApiKeyAuth<TResolver>()) outside Development.
```

Keys added with `AddHashed`, and keys a resolver returns, are not inspected. The check is a
backstop, not the gate: register demo keys inside `if (builder.Environment.IsDevelopment())` so a
deployed host never has them at all.

## Return Semantics

| Header state | Result |
|---|---|
| Absent | `AuthenticateResult.NoResult()` (permits `[AllowAnonymous]`) |
| Present more than once | `AuthenticateResult.Fail("Multiple API keys presented.")`. The resolver is not invoked. |
| Present, resolver returns `null` | `AuthenticateResult.Fail(...)` |
| Present, resolver throws | `AuthenticateResult.Fail(...)` (logged at `Warning`, not `Error`) |
| Present, resolver returns `TraxPrincipal` | `AuthenticateResult.Success(ticket)` |

## Protecting Endpoints

`AddTraxApiKeyAuth` does not set a default authentication scheme. That does not matter for the
Trax GraphQL endpoint: Trax authenticates every GraphQL HTTP request against each registered
scheme in registration order and keeps the first that succeeds, so `[TraxAuthorize]`,
`GateOperations(...)` and the builder's `RequireAuthorization()` all see an API-key caller with
no extra wiring. Gate GraphQL with those:

```csharp
// Gate every GraphQL operation (the combined Trax policy: any registered Trax scheme)
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());

// Or only API-key callers
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(ApiKeyDefaults.PolicyName));
```

Prefer the builder's `RequireAuthorization()` to an endpoint convention on the GraphQL route: the
startup checks for the `operations` namespace and `[TraxAllowAnonymous]` see the builder's gate
and do not see a route convention (see [Authorization](/docs/authorization#combining-with-endpoint-level-auth)).

For your own minimal-API or MVC routes, ASP.NET Core invokes a handler only for the schemes a
policy names, and a plain `[Authorize]` or `.RequireAuthorization()` names none. Name the policy:

```csharp
// Only API-key callers
app.MapGet("/reports", ...).RequireAuthorization(ApiKeyDefaults.PolicyName);

// Any Trax scheme (API key OR JWT OR ...)
app.MapGet("/me", ...).RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);
```

`TraxAuthClaimTypes.TraxAuthPolicy` is the name every Trax auth package registers into. Each `AddTrax*Auth` call adds its scheme to that policy's allowed-schemes list, so a route protected by `TraxAuthPolicy` accepts credentials from any configured Trax scheme.

If you want API-key auth to act as the default for the whole app, pass the scheme name to `AddAuthentication` directly:

```csharp
services.AddAuthentication(ApiKeyDefaults.SchemeName);
services.AddTraxApiKeyAuth(keys => keys.Add("...", id: "..."));
```

That is an application-level choice, not one Trax makes for you.
