---
title: HashedApiKeyResolver
description: Reference for HashedApiKeyResolver, the constant-time resolver over salted SHA-256 API-key hashes that AddTraxApiKeyAuth builds from a static key set.
type: reference
area: security
parent: API Auth
grand_parent: SDK Reference
nav_order: 7
package: Trax.Api.Auth.ApiKey
namespace: Trax.Api.Auth.ApiKey
source:
  - Trax.Api/src/Trax.Api.Auth.ApiKey/HashedApiKeyResolver.cs
---

# HashedApiKeyResolver

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

An `ITraxPrincipalResolver<string>` over a fixed set of salted SHA-256 key hashes. `AddTraxApiKeyAuth(keys => ...)` builds one from the keys `ApiKeyBuilder` collected and registers it as a singleton.

## Signature

```csharp fragment
public sealed class HashedApiKeyResolver : ITraxPrincipalResolver<string>
{
    public HashedApiKeyResolver(IEnumerable<Entry> entries);

    public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct);

    public sealed record Entry(byte[] Salt, byte[] Sha256Hash, Func<TraxPrincipal> PrincipalFactory)
    {
        public static Entry FromPlainKey(string cleartextKey, Func<TraxPrincipal> principalFactory, int saltBytes = 16);
    }
}
```

## Members

| Member | Description |
|---|---|
| `HashedApiKeyResolver(entries)` | Copies the entries. Later changes to the source are not seen. |
| `ResolveAsync(input, ct)` | Hashes `input` with each entry's salt and compares it with `CryptographicOperations.FixedTimeEquals`, against every entry whether or not one already matched. Returns the first matching entry's `PrincipalFactory()` result, or `null` for an empty input or no match. |
| `Entry.Salt` | The salt, raw bytes. |
| `Entry.Sha256Hash` | `SHA-256(salt || UTF-8(key))`, raw bytes. |
| `Entry.PrincipalFactory` | Called only when the entry matches. |
| `Entry.FromPlainKey(cleartextKey, principalFactory, saltBytes)` | Builds an entry from a cleartext key with a fresh random salt of `saltBytes` bytes. Throws for a blank key, a null factory, or `saltBytes` of 0 or less. |

## Behavior

The time a lookup takes does not depend on which entry, if any, matches. The entry set is fixed once built: rotating a key means building a new resolver.

## Example

```csharp compile
var resolver = new HashedApiKeyResolver([
    HashedApiKeyResolver.Entry.FromPlainKey(
        "ci-key-do-not-use-in-production",
        () => new TraxPrincipal("ci", "CI", ["Deployer"], PrincipalType: "apikey")),
]);

var principal = await resolver.ResolveAsync("ci-key-do-not-use-in-production", ct);
```

## See also

- [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth), [ITraxPrincipalResolver](/docs/sdk-reference/api-auth/i-trax-principal-resolver)
