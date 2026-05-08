---
layout: default
title: UsePersistedOperations
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

Minimal (DB hit per request, no cache, no broadcaster):

```csharp
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .UseDatabase(connectionString)
        .RequirePersisted(true)
    )
);
```

With cache + multi-node invalidation:

```csharp
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(opts => opts
        .UseDatabase(connectionString)
        .RequirePersisted(true)
        .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(15)))
        .UseRabbitMqInvalidation(rabbitConnectionString)
    )
);
```

See [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder) for every method.

## What gets registered

| Service | Lifetime | Purpose |
|---|---|---|
| `PersistedOperationsOptions` | Singleton | Resolved configuration. |
| `IDbContextFactory<PersistedOperationsDbContext>` | Singleton (factory) | EF Core context factory. Postgres-backed. |
| `IPersistedOperationCache` -> `NoOp` or `InMemory` | Singleton | Cache layer. No-op unless `WithInMemoryCache()` was called. |
| `IPersistedOperationBroadcaster` -> `NoOp` or `RabbitMq` | Singleton | Multi-node invalidation. No-op unless `UseRabbitMqInvalidation()` was called. |
| `PersistedOperationReceiverService` | Hosted (when broadcaster is RabbitMQ) | Subscribes to the fanout exchange and clears local cache on broadcast. |
| `IPersistedOperationStore` -> `DbPersistedOperationStorage` | Singleton | Programmatic CRUD. |
| `IOperationDocumentStorage` -> `DbPersistedOperationStorage` | Singleton | HotChocolate hot-path lookup. |
| `AllowlistMatcher` | Singleton | Used by the enforcement middleware. |
| `TimeProvider` | Singleton (TryAdd) | Default `TimeProvider.System`; override for tests. |

## Validation

Configuration errors throw `InvalidOperationException` at startup. Each message names the misconfigured method and suggests a fix:

| Misconfig | Behavior |
|---|---|
| `UseDatabase` not called | Throws: "UseDatabase(connectionString) is required." |
| `RequirePersisted(false)` and `LogNonPersistedRequests(false)` | Throws: configuration does nothing. |
| `UseRabbitMqInvalidation` without `WithInMemoryCache` | Throws: broadcasts have nothing to invalidate. |
| `WithInMemoryCache` called twice | Throws. |
| `AllowOperations` contains an empty entry | Throws. |
