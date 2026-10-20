---
title: PersistedOperationsBuilder
description: "Reference for PersistedOperationsBuilder: enforcement, the allowlist, node invalidation, caching, the management gate and machine document retention."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 2
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Configuration
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Enforcement.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Allowlist.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Broadcasting.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Cache.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Management.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.MachineDocuments.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Database.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Build.cs
---

# PersistedOperationsBuilder

The options passed to [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations). Every method returns the builder.

## Signature

```csharp fragment
public sealed partial class PersistedOperationsBuilder
{
    public PersistedOperationsBuilder RequirePersisted(bool require = true);
    public PersistedOperationsBuilder LogNonPersistedRequests(bool log = true);
    public PersistedOperationsBuilder AllowOperations(params string[] operationNames);
    public PersistedOperationsBuilder AllowOperationsMatching(Func<string, bool> predicate);
    public PersistedOperationsBuilder DisableIntrospection();
    public PersistedOperationsBuilder WithInMemoryCache(Action<CacheOptions>? configure = null);
    public PersistedOperationsBuilder WithCacheMaxAge(TimeSpan maxAge);
    public PersistedOperationsBuilder UseRabbitMqInvalidation(string connectionString);
    public PersistedOperationsBuilder SingleNode();
    public PersistedOperationsBuilder GateManagement(string? policy = null, string? roles = null);
    public PersistedOperationsBuilder GateManagementToAuthenticatedUsers();
    public PersistedOperationsBuilder KeepOlderMachineDocumentsFor(TimeSpan grace);
    public PersistedOperationsBuilder ClientDocuments(string path);
    public PersistedOperationsBuilder UseDatabase(string connectionString); // obsolete
}

public sealed class CacheOptions
{
    public TimeSpan? Ttl { get; }
    public CacheOptions WithTtl(TimeSpan ttl);
}
```

## Members

| Method | Default | Purpose |
|---|---|---|
| `RequirePersisted(bool require = true)` | `true` | Refuse inline documents. `false` is shadow mode, which needs `LogNonPersistedRequests(true)`. |
| `LogNonPersistedRequests(bool log = true)` | `false` | Log every inline-document request at Information. |
| `AllowOperations(params string[] names)` | empty | Operation names that bypass enforcement. Case-sensitive. Matched against the caller-supplied `operationName`, not the document: a convenience for trusted networks, not a security control. |
| `AllowOperationsMatching(Func<string, bool>)` | empty | Predicate form, such as `id => id.StartsWith("dev_")`. The predicate sees the caller-supplied `operationName` (or `documentId`), never the document, so it is as narrow as the network in front of the endpoint. |
| `DisableIntrospection()` | introspection allowed | Inline introspection requests are refused like any other inline document. |
| `WithInMemoryCache(Action<CacheOptions>?)` | no cache | Cache the store's lookups in `IMemoryCache`, under the document and prepared-operation caches, which are always on. Without it the database is read when a node first serves an id, after a change empties those caches, and when an entry reaches its maximum age. |
| `WithCacheMaxAge(TimeSpan maxAge)` | 5 minutes | The longest any cache on a node keeps a persisted operation, counted from when its document was read from the database. The backstop for a change that did not reach the node: it serves what it cached for at most this long, then reads the store again. Must be positive. |
| `UseRabbitMqInvalidation(string connectionString)` | none | Broadcast every change over a RabbitMQ fanout so every node empties its caches. Required when more than one node serves the endpoint. Works with or without `WithInMemoryCache()`. A publish the broker does not confirm is reported as `CHANGE_NOT_BROADCAST`. |
| `SingleNode()` | none | Declare that one process serves the endpoint and writes the store, so no broadcast is needed. |
| `GateManagement(string? policy = null, string? roles = null)` | not served | Serve the [management fields](/docs/sdk-reference/persisted-operations/management-mutations), `persistedOperations { ... }` at the root, behind `@authorize` with this policy or these roles. A comma-separated `roles` list is any of; repeated calls are all of. A policy or roles is required. Without this call or the next, the fields are not in the schema. |
| `GateManagementToAuthenticatedUsers()` | not served | Serve the management fields to any authenticated caller. On a host with public sign-up that is anyone, so prefer `GateManagement`. |
| `KeepOlderMachineDocumentsFor(TimeSpan grace)` | 7 days | How long a version of the [generated documents](/docs/sdk-reference/graphql-api/machines#persisted-documents) stays active once no node registers it, older or newer. Each node registers its own documents when it starts and again every hour (at most a quarter of this) while it runs, so a node still running another version keeps serving its documents; a version no node has registered for this long is deactivated. Must be positive. The same period applies to [client documents](/docs/api/fetch-only-what-a-component-needs). |
| `ClientDocuments(string path)` | none | Register the documents a frontend wrote, from the `trax.documents.json` that `trax client --documents` writes, when the host starts. `path` resolves against the content root; repeated calls add files. Each document is one named query or mutation of exposed trains under `dispatch` or `discover`, with no directive, and must pass the host's own validation. A missing or malformed file, a name used twice, or a document breaking either rule refuses the start, naming it. Each is stored under `trax_client_<name>_<hash of its text>`; a version a frontend no longer holds keeps running while it validates, until no node has registered it for the grace period above. |

`UseDatabase(string)` is obsolete, hidden from IntelliSense, and does nothing beyond throwing `ArgumentException` for a blank string: storage reads and writes `trax.persisted_operation` through the Trax data context that `AddEffects(e => e.UsePostgres(...))` registers.

## CacheOptions

| Method | Default | Purpose |
|---|---|---|
| `WithTtl(TimeSpan ttl)` | the maximum age | Entry lifetime for the Trax lookup cache, read back from `Ttl`. Must be positive (`ArgumentOutOfRangeException`) and may not exceed `WithCacheMaxAge` (`InvalidOperationException` when the options are built). |

## Behavior

- Exactly one of `SingleNode()` and `UseRabbitMqInvalidation(...)` is required. See [One node or many](/docs/api/persisted-operations#one-node-or-many).
- `GateManagement` throws `InvalidOperationException` with neither a policy nor roles, or with a role list that holds only separators. A policy it names that is not registered with `AddAuthorization` fails startup.
- `ClientDocuments` throws `ArgumentException` for a blank path.
- `UseRabbitMqInvalidation` throws `ArgumentException` for a blank connection string; `WithCacheMaxAge` and `KeepOlderMachineDocumentsFor` throw `ArgumentOutOfRangeException` for a value that is not positive; a second `WithInMemoryCache` throws `InvalidOperationException`.
- The remaining checks run when `UsePersistedOperations` builds the options and throw `InvalidOperationException`; the [UsePersistedOperations validation table](/docs/sdk-reference/persisted-operations/use-persisted-operations#validation) lists them.
- What the allowlist does and does not protect: [Allowlist and dev carve-outs](/docs/api/persisted-operations#allowlist-and-dev-carve-outs).

## Example

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .LogNonPersistedRequests(true)
        .AllowOperations("playground_smoke_test")
        .AllowOperationsMatching(id => id.StartsWith("dev_"))
        .WithCacheMaxAge(TimeSpan.FromMinutes(5))
        .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(2)))
        .UseRabbitMqInvalidation(rabbitMqConnectionString)
        .GateManagement(roles: "Deployer")
    )
);
```

## See also

- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
- [PersistedOperationsOptions](/docs/sdk-reference/persisted-operations/persisted-operations-options)
- [Persisted Operations](/docs/api/persisted-operations)
