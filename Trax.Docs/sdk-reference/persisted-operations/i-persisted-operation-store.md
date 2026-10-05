---
layout: default
title: IPersistedOperationStore
description: Reference for IPersistedOperationStore, programmatic CRUD over the trax.persisted_operation table for CI uploaders, admin tooling and tests.
parent: Persisted Operations
grand_parent: SDK Reference
---

# IPersistedOperationStore

Programmatic CRUD for `trax.persisted_operation`. The HTTP request path does NOT use this interface; HotChocolate calls `IOperationDocumentStorage.TryReadAsync` instead. Use this interface from CI manifest uploaders, admin tooling, custom dashboards, and tests.

Registered as a singleton when [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) is called. For the surface the GraphQL fields and the dashboard share, which returns refusals as payload errors instead of throwing, use [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service).

A host without a GraphQL server registers it with `AddPersistedOperationStore(store => ...)`, and says how a change made through it reaches the GraphQL nodes, as `UsePersistedOperations` does. The store refuses to start with neither, or both:

| Method | Purpose |
|---|---|
| `UseRabbitMqInvalidation(string connectionString)` | Broadcast every change over the broker the GraphQL nodes use with their own `UseRabbitMqInvalidation`, so each empties its caches. |
| `SingleNode()` | Declare that no other process caches these operations: this process serves them itself, or nothing serves them while it writes. Nothing checks the claim; a GraphQL node elsewhere sees the change only when its cached entry reaches its maximum age. |

```csharp
services.AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(rabbitConnectionString));
```

The store reads and writes through the Trax data context registered by `AddEffects(e => e.UsePostgres(...))`. The older overloads that took a database connection string are obsolete: `AddPersistedOperationStore(databaseConnectionString)` is a compile error and refuses at startup, and `AddPersistedOperationStore(databaseConnectionString, rabbitConnectionString)` ignores the first string and forwards to the broker form.

## Interface

```csharp
public interface IPersistedOperationStore
{
    Task<PersistedOperation?> GetAsync(string id, string? tenantKey, CancellationToken ct);

    Task<IReadOnlyList<PersistedOperation>> ListAsync(string? tenantKey, CancellationToken ct);

    Task<PersistedOperation> UpsertAsync(
        string id,
        string document,
        UpsertOptions? options,
        CancellationToken ct);

    Task DeactivateAsync(string id, string? tenantKey, string reason, CancellationToken ct);

    Task RestoreAsync(string id, string? tenantKey, CancellationToken ct);
}
```

## UpsertOptions

| Property | Type | Purpose |
|---|---|---|
| `TenantKey` | `string?` | Tenant scope for multi-tenant deployments. Null targets the single-tenant row set. |
| `Description` | `string?` | Operator-facing note recorded on the row. |
| `BypassShapeDiff` | `bool` | When true, skips the shape-diff guardrail on an edit. Use only when the operator has verified the change is shape-safe. |
| `Version` | `int` | Operator-controlled metadata stored on the row. Not used for request routing. Defaults to `0`. |

## Behavior

- `GetAsync` returns null for missing or deactivated rows.
- `ListAsync` returns active and deactivated rows for the tenant.
- `UpsertAsync` runs the document through [IPersistedOperationValidator](/docs/sdk-reference/persisted-operations/i-persisted-operation-validator) (HotChocolate-backed when `UsePersistedOperations` is wired in), extracts the GraphQL operation name from the document's operation definition, computes the response-shape fingerprint, writes both the live row and a history row in a single transaction, invalidates the local caches, and publishes a broadcast event when the broadcaster is configured.
- Changes to one id are applied one at a time. Each write takes a transaction-scoped lock on the id (a PostgreSQL advisory lock; SQLite's write transaction) before it reads the row, so two first uploads of one id do not collide, and every upload's shape-diff check sees the row the previous change left.
- Once a change is committed it is applied to this node's caches and broadcast whether or not the caller's token is still live. When the broker does not confirm the broadcast, the call throws `PersistedOperationNotBroadcastException` (`CHANGE_NOT_BROADCAST`): the change is saved and in force here, and other nodes pick it up when their cached entry reaches its maximum age. Repeat the change to send it again.
- A document with no operation, or more than one, throws `PersistedOperationInputException` (`INVALID_INPUT`).
- Validation throws one of the structured [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) subclasses. No row is written and no broadcast fires when validation rejects the document.
- `DeactivateAsync` and `RestoreAsync` throw `InvalidOperationException` when the id does not exist. Both act on a row in either state, so repeating one sends the change again.
- All mutations append a row to `trax.persisted_operation_history`.

## Example

Manifest uploader:

```csharp
var store = serviceProvider.GetRequiredService<IPersistedOperationStore>();

foreach (var (id, document) in manifest)
{
    await store.UpsertAsync(id, document, options: null, ct);
}
```

Soft-delete with reason:

```csharp
await store.DeactivateAsync(
    "userProfile_v1",
    tenantKey: null,
    reason: "broken filter in 2026-05-08 release",
    ct
);
```
