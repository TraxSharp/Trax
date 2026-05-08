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
| `BypassShapeDiff` | `bool` | Reserved for the v1.1 dashboard `--force` path. Currently a no-op. |

## Behavior

- `GetAsync` returns null for missing or deactivated rows.
- `ListAsync` returns active and deactivated rows for the tenant.
- `UpsertAsync` parses the id (`name_vN`), computes the response-shape fingerprint, writes both the live row and a history row in a single transaction, invalidates the local cache, and publishes a broadcast event when the broadcaster is configured.
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
