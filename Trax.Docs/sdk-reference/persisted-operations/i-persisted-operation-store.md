---
layout: default
title: IPersistedOperationStore
parent: Persisted Operations
grand_parent: SDK Reference
---

# IPersistedOperationStore

Programmatic CRUD for `trax.persisted_operation`. The HTTP request path does NOT use this interface; HotChocolate calls `IOperationDocumentStorage.TryReadAsync` instead. Use this interface from CI manifest uploaders, admin tooling, custom dashboards, and tests.

Registered as a singleton when [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) is called.

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
- `UpsertAsync` runs the document through [IPersistedOperationValidator](/docs/sdk-reference/persisted-operations/i-persisted-operation-validator) (HotChocolate-backed when `UsePersistedOperations` is wired in), extracts the GraphQL operation name from the document's operation definition, computes the response-shape fingerprint, writes both the live row and a history row in a single transaction, invalidates the local cache, and publishes a broadcast event when the broadcaster is configured.
- Validation throws one of the structured [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) subclasses. No row is written and no broadcast fires when validation rejects the document.
- `DeactivateAsync` and `RestoreAsync` throw `InvalidOperationException` when the id does not exist.
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
