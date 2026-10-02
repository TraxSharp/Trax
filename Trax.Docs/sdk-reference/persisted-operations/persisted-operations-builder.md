---
layout: default
title: PersistedOperationsBuilder
description: "Reference for PersistedOperationsBuilder: single-node or RabbitMQ invalidation, cache options, the allowlist and the validation rules it applies at startup."
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperationsBuilder

Fluent configuration surface passed to [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations). All methods return `PersistedOperationsBuilder` for chaining.

## Methods

| Method | Default | Purpose |
|---|---|---|
| `UseDatabase(string connectionString)` | required | A connection string the builder requires (it throws if missing) but does not use: the store reads and writes `trax.persisted_operation` through the Trax Effect data provider (`UsePostgres`), on that provider's database. |
| `RequirePersisted(bool require = true)` | `true` | Reject inline-query requests. Set false for shadow mode. |
| `LogNonPersistedRequests(bool log = true)` | `false` | Log every inline-query request at Information. Use during phased rollout. |
| `AllowOperations(params string[] names)` | empty | Operation names that bypass enforcement. Case-sensitive. Matched against the caller-supplied `operationName`, not the document: a convenience for trusted networks, not a security control. |
| `AllowOperationsMatching(Func<string, bool>)` | empty | Predicate form. Useful for `id => id.StartsWith("dev_")`. The predicate sees the caller-supplied `operationName` (or `documentId`), never the document, so it is as narrow as the network in front of the endpoint. |
| `DisableIntrospection()` | introspection allowed | Reject introspection requests. Use only for strict prod. |
| `WithInMemoryCache(Action<CacheOptions>?)` | no cache | Cache the store's lookups in `IMemoryCache`, under HotChocolate's own document and prepared-operation caches, which are always on. Without it the database is read when a node first serves an id and after a change empties those caches. |
| `UseRabbitMqInvalidation(string connectionString)` | none | Broadcast every change over a RabbitMQ fanout so every node empties its caches. Required when more than one node serves the endpoint. Works with or without `WithInMemoryCache()`. |
| `SingleNode()` | none | Declare that one process serves the endpoint and writes the store, so no broadcast is needed. |

One of `SingleNode()` and `UseRabbitMqInvalidation(...)` is required, and not both. See [One node or many](/docs/persisted-operations#one-node-or-many).

See [Allowlist and dev carve-outs](/docs/persisted-operations#allowlist-and-dev-carve-outs) for what the allowlist does and does not protect.

## CacheOptions

| Method | Default | Purpose |
|---|---|---|
| `WithTtl(TimeSpan ttl)` | 15 minutes | Entry lifetime for the Trax lookup cache only. HotChocolate's caches do not expire, so on more than one node the broadcast keeps them current, not this. |

## Example

```csharp
opts
    .UseDatabase(connectionString)
    .RequirePersisted(true)
    .LogNonPersistedRequests(true)
    .AllowOperations("playground_smoke_test")
    .AllowOperationsMatching(id => id.StartsWith("dev_"))
    .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(10)))
    .UseRabbitMqInvalidation("amqp://guest:guest@localhost:5672/");
```

## Validation rules

The builder's `Build()` runs at startup and throws `InvalidOperationException` for inconsistent configurations. See the [UsePersistedOperations validation table](/docs/sdk-reference/persisted-operations/use-persisted-operations#validation) for every check.
