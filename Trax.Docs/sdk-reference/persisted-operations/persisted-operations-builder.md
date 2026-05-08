---
layout: default
title: PersistedOperationsBuilder
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperationsBuilder

Fluent configuration surface passed to [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations). All methods return `PersistedOperationsBuilder` for chaining.

## Methods

| Method | Default | Purpose |
|---|---|---|
| `UseDatabase(string connectionString)` | required | Postgres connection string for the persisted-operation tables. Throws if missing. |
| `RequirePersisted(bool require = true)` | `true` | Reject inline-query requests. Set false for shadow mode. |
| `LogNonPersistedRequests(bool log = true)` | `false` | Log every inline-query request at Information. Use during phased rollout. |
| `AllowOperations(params string[] names)` | empty | Operation names that bypass enforcement. Case-sensitive. |
| `AllowOperationsMatching(Func<string, bool>)` | empty | Predicate form. Useful for `id => id.StartsWith("dev_")`. |
| `DisableIntrospection()` | introspection allowed | Reject introspection requests. Use only for strict prod. |
| `WithInMemoryCache(Action<CacheOptions>?)` | no cache | Wrap storage with `IMemoryCache`. The default path hits the DB on every request. |
| `UseRabbitMqInvalidation(string connectionString)` | no broadcaster | Multi-node cache invalidation via RabbitMQ fanout. Requires `WithInMemoryCache()`. |

## CacheOptions

| Method | Default | Purpose |
|---|---|---|
| `WithTtl(TimeSpan ttl)` | 15 minutes | Cache entry lifetime. Backstop only; broadcast invalidation is the primary mechanism. |

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
