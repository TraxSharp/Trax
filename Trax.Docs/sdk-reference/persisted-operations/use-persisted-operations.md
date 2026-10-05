---
layout: default
title: UsePersistedOperations
description: Reference for UsePersistedOperations, which wires persisted-operation storage, caching, cross-node invalidation, the allowlist and shadow-mode logging.
parent: Persisted Operations
grand_parent: SDK Reference
---

# UsePersistedOperations

Fluent extension on `TraxGraphQLBuilder` that wires the persisted-operation pipeline: storage, optional cache, optional cross-node invalidation, allowlist, introspection bypass, and shadow-mode logging.

## Signature

```csharp
public static TraxGraphQLBuilder UsePersistedOperations(
    this TraxGraphQLBuilder builder,
    Action<PersistedOperationsBuilder> configure
);
```

## Usage

One node:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .SingleNode()
    )
);
```

More than one node, with the optional Trax lookup cache:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .RequirePersisted(true)
        .WithInMemoryCache()
        .UseRabbitMqInvalidation(rabbitConnectionString)
    )
);
```

One of `SingleNode()` and `UseRabbitMqInvalidation(...)` is required; see [One node or many](/docs/persisted-operations#one-node-or-many).

See [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) for every method.

## What gets registered

| Service | Lifetime | Purpose |
|---|---|---|
| `PersistedOperationsOptions` | Singleton | Resolved configuration. |
| `IPersistedOperationCache` -> `NoOp` or `InMemory` | Singleton | Cache layer. No-op unless `WithInMemoryCache()` was called. |
| `IPersistedOperationBroadcaster` -> `NoOp` or `RabbitMq` | Singleton | Multi-node invalidation. RabbitMQ with `UseRabbitMqInvalidation()`, no-op with `SingleNode()`. |
| `PersistedOperationReceiverService` | Hosted (when broadcaster is RabbitMQ) | Subscribes to the fanout exchange and empties the document, prepared-operation and Trax caches on broadcast, on losing the broker connection, and on recovering it. When the broker closes its channel alone it empties them, subscribes again on a new channel, and empties them once more. |
| `IPersistedOperationStore` -> `DbPersistedOperationStorage` | Singleton | Programmatic CRUD. Reads and writes through the Effect data provider's `IDataContextProviderFactory`, so the host needs `UsePostgres` (or another data provider); the tables are sets on the Effect `DataContext`. |
| `IPersistedOperationsService` | Singleton (TryAdd) | The management surface the GraphQL fields and the dashboard call. |
| `IOperationDocumentStorage` -> `DbPersistedOperationStorage` | Singleton | HotChocolate hot-path lookup. |
| `IPersistedOperationValidator` -> `HotChocolateSchemaValidator` | Singleton (Replace) | Runs HotChocolate validation against the live schema before every upsert. Overrides the no-op default from `AddPersistedOperationStore`. |
| `IPersistedOperationsCapability` | Singleton | Marker meaning this process serves the management GraphQL fields. The dashboard gates its pages on `IPersistedOperationsService` instead, so they also appear on a host that registers only `AddPersistedOperationStore`. |
| `AllowlistMatcher` | Singleton | Used by the enforcement middleware. |
| `PersistedOperationPolicy` | Singleton | The enforcement decision, asked once per operation by the request middleware `UsePersistedOperations` adds after HotChocolate's document parser. |
| `TimeProvider` | Singleton (TryAdd) | Default `TimeProvider.System`; override for tests. The caches' maximum age is measured on it. |
| `IDocumentCache`, `IPreparedOperationCache` | Singleton (schema services) | Replacements for HotChocolate's caches, sized from the host's `ModifyOptions` (`OperationDocumentCacheSize`, `PreparedOperationCacheSize`). Each entry carries the generation it was written in and the time its document was read; it is served only while no change has been applied since and it is younger than `WithCacheMaxAge`. The executor refuses to build if anything else replaces them. |

Also extends the GraphQL schema with the [management mutations and queries](/docs/sdk-reference/persisted-operations/management-mutations), and calls `ExposeOperationQueries()` / `ExposeOperationMutations()` on the builder so `RootQuery` and `RootMutation` are emitted even when the host has not registered any train-backed queries or mutations.

Because this exposes the `operations` namespace (including the persisted-operation management mutations, which are admin operations), the host must answer for it, or `AddTraxGraphQL()` fails at startup: [`GateOperations(policy, roles)`](/docs/sdk-reference/graphql-api/add-trax-graphql) to gate the namespace while the rest of the endpoint stays open, `RequireAuthorization()` to gate the whole endpoint, or `AllowAnonymousOperations()` to opt into anonymous access.

### Declining the namespace

Persisted operations and a GraphQL-exposed scheduler console are separable. `ExposeOperationsNamespace(false)` wires storage, enforcement, the cache and cross-node invalidation without touching the schema, for a host that manages its operations out of band (a migration, a deploy step, a separate admin process):

```csharp
.UsePersistedOperations(po => po
    .RequirePersisted(true)
    .SingleNode()
    .ExposeOperationsNamespace(false)
)
```

With the namespace declined there is nothing to gate and nothing to acknowledge. The management mutations are not in the schema either: they are `[ExtendObjectType(typeof(OperationsMutations))]` classes, and their target type is not there to extend.

## Validation

Configuration errors throw `InvalidOperationException` at startup. Each message names the misconfigured method and suggests a fix:

| Misconfig | Behavior |
|---|---|
| `RequirePersisted(false)` and `LogNonPersistedRequests(false)` | Throws: configuration does nothing. |
| Neither `SingleNode()` nor `UseRabbitMqInvalidation(...)` | Throws: names both, since a change reaches another node at once only by broadcast. |
| Both `SingleNode()` and `UseRabbitMqInvalidation(...)` | Throws: the two contradict each other. |
| `WithInMemoryCache` called twice | Throws. |
| `WithInMemoryCache` TTL longer than `WithCacheMaxAge` | Throws: the TTL would not take effect. |
| `WithCacheMaxAge` not positive | Throws `ArgumentOutOfRangeException` when called. |
| `AllowOperations` contains an empty entry | Throws. |
