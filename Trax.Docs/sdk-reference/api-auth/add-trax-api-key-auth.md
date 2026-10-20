---
title: AddTraxApiKeyAuth
description: "Reference for AddTraxApiKeyAuth: static key sets and DI-scoped resolvers, ApiKeyBuilder methods, hashed key comparison, demo keys and the policies it registers."
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 1
package: Trax.Api.Auth.ApiKey
namespace: Trax.Api.Auth.ApiKey
source:
  - Trax.Api/src/Trax.Api.Auth.ApiKey/ApiKeyAuthServiceCollectionExtensions.cs
  - Trax.Api/src/Trax.Api.Auth.ApiKey/ApiKeyBuilder.cs
  - Trax.Api/src/Trax.Api.Auth.ApiKey/ApiKeyAuthHandler.cs
  - Trax.Api/src/Trax.Api.Auth.ApiKey/DemoApiKeyEnvironmentValidator.cs
---

# AddTraxApiKeyAuth

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension methods on `IServiceCollection`, declared on `ApiKeyAuthServiceCollectionExtensions`, that register the Trax API-key authentication scheme. One overload takes a static key set built with `ApiKeyBuilder`; the other takes a resolver from DI.

## Signatures

```csharp fragment
public static AuthenticationBuilder AddTraxApiKeyAuth(
    this IServiceCollection services,
    Action<ApiKeyBuilder> configure,
    Action<ApiKeyAuthenticationOptions>? configureOptions = null);

public static AuthenticationBuilder AddTraxApiKeyAuth<TResolver>(
    this IServiceCollection services,
    Action<ApiKeyAuthenticationOptions>? configureOptions = null)
    where TResolver : class, ITraxPrincipalResolver<string>;
```

```csharp fragment
public sealed class ApiKeyBuilder
{
    public const string DemoKeyMarker = "do-not-use-in-production";

    public ApiKeyBuilder Add(string key, string id, params string[] roles);
    public ApiKeyBuilder Add(string key, Func<TraxPrincipal> principalFactory);
    public ApiKeyBuilder AddHashed(byte[] salt, byte[] sha256, string id, params string[] roles);
    public ApiKeyBuilder AddHashed(byte[] salt, byte[] sha256, Func<TraxPrincipal> principalFactory);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `configure` | `Action<ApiKeyBuilder>` | Registers the static key set. At least one key is required. |
| `TResolver` | type argument | An [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver)`<string>` that receives the raw header value. Registered scoped. |
| `configureOptions` | `Action<ApiKeyAuthenticationOptions>?` | Sets [ApiKeyAuthenticationOptions](/docs/sdk-reference/api-auth/api-key-authentication-options), such as the header name. |

## Members

`ApiKeyBuilder`, passed to `configure`:

| Member | Description |
|---|---|
| `Add(string key, string id, params string[] roles)` | A cleartext key. The principal's id is also its display name, and its `PrincipalType` is `apikey`. Roles are optional. |
| `Add(string key, Func<TraxPrincipal> principalFactory)` | A cleartext key whose principal the factory builds whole: display name, claims, and `PrincipalType`, which the factory sets itself if anything reads it (the audit trail does). |
| `AddHashed(byte[] salt, byte[] sha256, string id, params string[] roles)` | A pre-hashed key: cleartext never enters the process. Builds the same principal as `Add(key, id, roles)`. |
| `AddHashed(byte[] salt, byte[] sha256, Func<TraxPrincipal> principalFactory)` | A pre-hashed key with a principal factory. |
| `DemoKeyMarker` | `do-not-use-in-production`, the marker of a published demo key. |

`sha256` is `SHA-256(salt || UTF-8(key))`, the salt bytes followed by the key's UTF-8 bytes. `AddHashed` throws `ArgumentException` for an empty salt or a hash that is not 32 bytes. `Add` and `AddHashed` throw for a null or blank key or id.

## Returns

The `AuthenticationBuilder` from `services.AddAuthentication()`, for chaining further schemes.

## Behavior

Keys registered through the builder are salted with 16 random bytes and SHA-256 hashed when they are registered, held by a singleton [HashedApiKeyResolver](/docs/sdk-reference/api-auth/hashed-api-key-resolver), and compared with `CryptographicOperations.FixedTimeEquals` against every entry on each request. The builder overload throws `InvalidOperationException` when no key was registered. On the resolver overload, comparing keys is the resolver's job.

A principal this scheme authenticates carries the id `TraxApiKey:{id}`. See [Qualified principal ids](/docs/migration-guides/qualified-principal-ids).

### Authentication results

| Request | Result |
|---|---|
| No key header | `AuthenticateResult.NoResult()`: other schemes run, and an anonymous endpoint stays open. |
| Header present but empty or whitespace | `AuthenticateResult.NoResult()` |
| Header present more than once | `Fail("Multiple API keys presented.")`. The resolver is not called. |
| One header whose value contains a comma | `Fail("Ambiguous API key header.")`. The resolver is not called. |
| Resolver returns `null` | `Fail("Invalid API key.")` |
| Resolver throws | `Fail("API-key resolver failed.")`, the exception logged at `Warning`. |
| Resolver returns a principal | `Success`, with the principal's claims under the `TraxApiKey` scheme. |

### Demo keys start only in Development

A key added with `Add` whose text contains `DemoKeyMarker`, compared ignoring case, makes the host refuse to start in any environment other than Development, before any hosted service or Kestrel starts. The message names the marker and the environment, never the key:

```
AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production', which marks a
published demo key, and the environment is 'Production'. Such keys start only in Development.
Register real keys (keys.AddHashed(...) from a secret store, or a resolver via
AddTraxApiKeyAuth<TResolver>()) outside Development.
```

Keys added with `AddHashed`, and keys a resolver accepts, are not inspected.

### Policies and default scheme

`AddTraxApiKeyAuth` registers the policy `ApiKeyDefaults.PolicyName`, satisfied only by a caller this scheme authenticated, and adds the scheme to the combined `TraxAuthClaimTypes.TraxAuthPolicy`. It does not set a default authentication scheme. The Trax GraphQL endpoint authenticates each request against every registered scheme in registration order and keeps the first that succeeds, so `[TraxAuthorize]`, `GateOperations(...)` and the builder's `RequireAuthorization()` see an API-key caller. On other routes ASP.NET Core runs only the schemes a policy names, and a plain `RequireAuthorization()` names none: name `ApiKeyDefaults.PolicyName` or `TraxAuthPolicy`.

### Registered services

| Service | Lifetime |
|---|---|
| Authentication services (`AddAuthentication()`) and the `TraxApiKey` scheme with its handler | |
| `ITraxPrincipalResolver<string>`: `HashedApiKeyResolver` (builder overload) or `TResolver` | Singleton or scoped |
| `ApiKeyPolicy`, and `TraxApiKey` in `TraxAuthPolicy` | |
| [`TraxPrincipal` and `TraxCaller`](/docs/sdk-reference/api-auth/add-trax-principal-accessor), through `AddTraxPrincipalAccessor()`, and `IHttpContextAccessor` | Scoped |
| The demo-key startup check, when a marked key was added | Hosted service |
| The NO WARRANTY startup log, registered once | Hosted service |

A host that calls `AddTraxApiKeyAuth` conditionally also calls `services.AddAuthentication()` and `services.AddTraxPrincipalAccessor()` unconditionally: without the first, `app.UseAuthentication()` throws when no scheme was registered, and without the second the mediator refuses to start a host whose junctions inject `TraxPrincipal`. Both are idempotent.

## Examples

### Static key set

Registered only in Development:

```csharp compile
if (builder.Environment.IsDevelopment())
{
    services.AddTraxApiKeyAuth(keys => keys
        .Add("admin-key-do-not-use-in-production", id: "admin", "Admin", "Player")
        .Add("player-key-do-not-use-in-production", id: "player", "Player")
        .Add("reader-key-do-not-use-in-production", id: "reader"));
}
```

### Principal factory

A key whose principal has a display name of its own:

```csharp compile
services.AddTraxApiKeyAuth(keys => keys
    .Add("alice-key-do-not-use-in-production",
        () => new TraxPrincipal("alice", "Alice Liddell", ["User"], PrincipalType: "apikey")));
```

### Pre-hashed keys

A key loaded from configuration as salt and hash:

```csharp compile
var admin = builder.Configuration.GetSection("ApiKeys:Admin");

services.AddTraxApiKeyAuth(keys => keys.AddHashed(
    salt: Convert.FromBase64String(admin["Salt"]!),
    sha256: Convert.FromBase64String(admin["Hash"]!),
    id: "admin",
    "Admin"));
```

### Producing a salt and hash

Once, when the key is issued:

```csharp compile
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); // give this to the caller
var salt = RandomNumberGenerator.GetBytes(16);
var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(key)]);

Console.WriteLine($"Salt: {Convert.ToBase64String(salt)}");
Console.WriteLine($"Hash: {Convert.ToBase64String(hash)}");
```

### Resolver

A resolver backed by a user store, and the policy on a route:

```csharp compile
services.AddTraxApiKeyAuth<DirectoryApiKeyResolver>();
services.AddTraxGraphQL(graphql => graphql.RequireAuthorization(ApiKeyDefaults.PolicyName));

app.MapGet("/reports", () => Results.Ok()).RequireAuthorization(ApiKeyDefaults.PolicyName);

public sealed class DirectoryApiKeyResolver(IUserDirectory users) : ITraxPrincipalResolver<string>
{
    public async ValueTask<TraxPrincipal?> ResolveAsync(string apiKey, CancellationToken ct)
    {
        var user = await users.FindByApiKeyHashAsync(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)), ct);
        return user is null
            ? null
            : new TraxPrincipal(user.Id, user.DisplayName, user.Roles, PrincipalType: "apikey");
    }
}
```

## See also

- [API-key authentication](/docs/security/secure-a-graphql-host#1-write-the-host)
- [HashedApiKeyResolver](/docs/sdk-reference/api-auth/hashed-api-key-resolver), [ApiKeyAuthenticationOptions](/docs/sdk-reference/api-auth/api-key-authentication-options), [ApiKeyDefaults](/docs/sdk-reference/api-auth/api-key-defaults)
- [Authorization](/docs/security/authorization-rules#combining-with-endpoint-level-auth)
- The [Auth sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth) registers keys conditionally.
