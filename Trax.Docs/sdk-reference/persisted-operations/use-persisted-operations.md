---
title: UsePersistedOperations
description: "Reference for UsePersistedOperations, which wires persisted-operation storage, enforcement, caching, cross-node invalidation and the management fields."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 1
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Extensions
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Extensions/TraxGraphQLBuilderPersistedOperationsExtensions.cs
---

# UsePersistedOperations

Extension on `TraxGraphQLBuilder`, declared on the static class `TraxGraphQLBuilderPersistedOperationsExtensions`, that wires the persisted-operation pipeline: storage, enforcement, the optional lookup cache, cross-node invalidation, the allowlist, the introspection bypass, shadow-mode logging and the management fields.

## Signature

```csharp fragment
public static TraxGraphQLBuilder UsePersistedOperations(
    this TraxGraphQLBuilder builder,
    Action<PersistedOperationsBuilder> configure
);
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `builder` | `TraxGraphQLBuilder` | The GraphQL builder passed to `AddTraxGraphQL` |
| `configure` | `Action<PersistedOperationsBuilder>` | Sets the options; see [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) |

## Returns

The same `TraxGraphQLBuilder`.

## Behavior

Exactly one of `SingleNode()` and `UseRabbitMqInvalidation(...)` is required; see
[One node or many](/docs/persisted-operations#one-node-or-many). The options are checked when
`UsePersistedOperations` runs (see [Validation](#validation)).

### Generated documents

With persisted operations on, the host registers a document for every exposed machine operation
([Persisted documents](/docs/sdk-reference/graphql-api/machines#persisted-documents)), so a host that requires
persisted operations serves its machines with no upload. It generates none for a train: a frontend reaches a train
through a document it wrote, which the host registers from
[`ClientDocuments(path)`](/docs/sdk-reference/persisted-operations/persisted-operations-builder) when it starts
([Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs)). A select-everything document of
a train grows with the number of paths through its answer, so an EF model whose entities reference each other either
passed the parser's limit or was refused for cost on every call; the decision is
[ADR 0054](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md).
The `trax_train_` prefix an earlier version registered train documents under stays reserved: such an id answers
`SERVER_OUTDATED` while a node of that version may still serve it, then `CLIENT_OUTDATED`, so a page built against
it reloads.

A train's output is resolved from the object the train returned, not projected from a query, so every field a
document selects is read on every call. A navigation the train did not load comes back null or empty, and with EF's
lazy-loading proxies on, each one runs a query. Select what the component needs, and load what the answer exposes.

A host where a train carries [`[TraxRunProgress]`](/docs/sdk-reference/graphql-api/run-progress) also registers the
two documents of `runProgress`, under `trax_progress_query_<shapeHash><textHash>` and
`trax_progress_subscription_<shapeHash><textHash>`, hashed the same way. One pair serves every such train.

A host that serves the operations console ([UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard#with-persisted-operations))
registers the console's own documents too, under `trax_console_<name>_<hash>`, hashed as a client
document is, and tells the console their ids. Each reaches only `operations` or one of the
operator's subscriptions; one the host cannot serve is left out and logged rather than refusing
the start.

A version of a train's or the progress documents is kept for as long as any node registers it, and collected once no
node has for the grace period, the way a machine's is
([collection](/docs/sdk-reference/graphql-api/machines#persisted-documents)): two hosts on one database that expose
one train field with two shapes keep both.

### Registered services

| Service | Lifetime | Purpose |
|---|---|---|
| `PersistedOperationsOptions` | Singleton (Replace) | Resolved configuration. Replaces the options `AddPersistedOperationStore` registers, in either order, so a process has one set. |
| `IPersistedOperationCache` -> `NoOp` or `InMemory` | Singleton | Cache layer. No-op unless `WithInMemoryCache()` was called. |
| `IPersistedOperationBroadcaster` -> `NoOp` or `RabbitMq` | Singleton | Multi-node invalidation. RabbitMQ with `UseRabbitMqInvalidation()`, no-op with `SingleNode()`. |
| `PersistedOperationReceiverService` | Hosted (when broadcaster is RabbitMQ) | Subscribes to the fanout exchange and empties the document, prepared-operation and Trax caches on broadcast, on losing the broker connection, and on recovering it. When the broker closes its channel alone it empties them, subscribes again on a new channel, and empties them once more. |
| `IPersistedOperationStore` -> `DbPersistedOperationStorage` | Singleton | Programmatic CRUD. Reads and writes through the Effect data provider's `IDataContextProviderFactory`, so the host needs `UsePostgres` (or another data provider); the tables are sets on the Effect `DataContext`. |
| `IPersistedOperationsService` | Singleton (TryAdd) | The management surface the GraphQL fields and the dashboard call. |
| `IPersistedOperationsManagementGate` | Scoped (Replace) | The declared management gate, for the dashboard's pages. Scoped so its policies are evaluated with the caller's services, as the fields' are. |
| `IOperationDocumentStorage` -> `DbPersistedOperationStorage` | Singleton | HotChocolate hot-path lookup. |
| `IPersistedOperationValidator` -> `HotChocolateSchemaValidator` | Singleton (Replace) | Runs HotChocolate validation against the live schema before every upsert. Overrides the no-op default from `AddPersistedOperationStore`. |
| `IPersistedOperationsCapability` | Singleton | Marker meaning this process runs `UsePersistedOperations`. The dashboard gates its pages on `IPersistedOperationsService` instead, so they also appear on a host that registers only `AddPersistedOperationStore`. |
| `AllowlistMatcher` | Singleton | Used by the enforcement middleware. |
| `PersistedOperationPolicy` | Singleton | The enforcement decision, asked once per operation by the request middleware `UsePersistedOperations` adds after HotChocolate's document parser. |
| `MachineDocumentRegistration` | Hosted (lifecycle) | Generates every exposed machine's [persisted documents](/docs/sdk-reference/graphql-api/machines#persisted-documents) from the built schema and registers them in the store before the host serves anything, writing nothing for a document already stored as generated. A failure stops the host. |
| `TimeProvider` | Singleton (TryAdd) | Default `TimeProvider.System`; override for tests. The caches' maximum age is measured on it. |
| `IDocumentCache`, `IPreparedOperationCache` | Singleton (schema services) | Replacements for HotChocolate's caches, sized from the host's `ModifyOptions` (`OperationDocumentCacheSize`, `PreparedOperationCacheSize`). Each entry carries the generation it was written in and the time its document was read; it is served only while no change has been applied since and it is younger than `WithCacheMaxAge`. The executor refuses to build if anything else replaces them. |

It adds nothing to the `operations` namespace. A host that wants `operations` exposes it itself, with `ExposeOperationQueries()` or `ExposeOperationMutations()`, and answers for it there.

### The management fields

The [management mutations and queries](/docs/sdk-reference/persisted-operations/management-mutations) sit at the root, `persistedOperations { ... }` on `RootQuery` and `RootMutation`, behind a gate of their own. They are served only once the host declares who may use them:

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(po => po
        .RequirePersisted(true)
        .SingleNode()
        .GateManagement(roles: "Deployer")
    )
);
```

`GateManagement(policy, roles)` puts `@authorize` on both `persistedOperations` fields, with the same rules as [`GateOperations`](/docs/sdk-reference/graphql-api/add-trax-graphql): a policy or roles is required, a comma-separated role list is any of, and repeated calls are all of. `GateManagementToAuthenticatedUsers()` admits any signed-in caller. The gate is separate from the operations gate, so a deploy pipeline that uploads documents holds the management role and none of the operator's, and an operator at the dashboard who manages documents holds both.

Without a management gate the fields are not in the schema and the Trax dashboard shows no management pages. Storage, enforcement, the cache and cross-node invalidation are wired either way, and documents are managed out of band: a deploy step or a migration calling `IPersistedOperationsService`. With a gate, the dashboard's pages check it for the operator through [IPersistedOperationsManagementGate](/docs/sdk-reference/persisted-operations/i-persisted-operations-service#ipersistedoperationsmanagementgate).

### Validation

Configuration errors throw `InvalidOperationException` when `UsePersistedOperations` runs, during service registration. Each message names the misconfigured method and suggests a fix:

| Misconfig | Behavior |
|---|---|
| `RequirePersisted(false)` and `LogNonPersistedRequests(false)` | Throws: configuration does nothing. |
| Neither `SingleNode()` nor `UseRabbitMqInvalidation(...)` | Throws: names both, since a change reaches another node at once only by broadcast. |
| Both `SingleNode()` and `UseRabbitMqInvalidation(...)` | Throws: the two contradict each other. |
| `WithInMemoryCache` called twice | Throws. |
| `WithInMemoryCache` TTL longer than `WithCacheMaxAge` | Throws: the TTL would not take effect. |
| `WithCacheMaxAge` not positive | Throws `ArgumentOutOfRangeException` when called. |
| `AllowOperations` contains an empty entry | Throws. |
| `GateManagement()` with neither a policy nor roles, or with a role list of only separators | Throws when called. |
| A policy `GateManagement` names is not registered | The host fails at startup, naming the policy and the field. |
| `AddPersistedOperationStore` in the same host declares a different `GateManagement` | The host fails at startup, naming both calls. |

## Examples

One node:

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .SingleNode()
    )
);
```

More than one node, with the optional Trax lookup cache:

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .WithInMemoryCache()
        .UseRabbitMqInvalidation(rabbitMqConnectionString)
        .GateManagement(policy: "TraxAdmin")
    )
);
```

## See also

- [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
- [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement), for the decision each request goes through
- [Management mutations and queries](/docs/sdk-reference/persisted-operations/management-mutations)
- [AddPersistedOperationStore](/docs/sdk-reference/persisted-operations/add-persisted-operation-store), for a host with no GraphQL server
- [Persisted Operations](/docs/persisted-operations)
