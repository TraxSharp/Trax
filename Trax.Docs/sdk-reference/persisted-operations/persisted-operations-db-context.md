---
layout: default
title: PersistedOperationsDbContext
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperationsDbContext

Focused EF Core context for `trax.persisted_operation` and `trax.persisted_operation_history`. Kept separate from `Trax.Effect.Data.DataContext` so the GraphQL feature does not pollute the train/manifest tracking layer.

Registered as `IDbContextFactory<PersistedOperationsDbContext>` by [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations).

## DbSets

| DbSet | Type | Table |
|---|---|---|
| `PersistedOperations` | `DbSet<PersistedOperation>` | `trax.persisted_operation` |
| `PersistedOperationHistories` | `DbSet<PersistedOperationHistory>` | `trax.persisted_operation_history` |

## Direct usage

For most consumers, [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) is the right entry point. Read directly from the DbContext only when you need raw EF features:

```csharp
var factory = sp.GetRequiredService<IDbContextFactory<PersistedOperationsDbContext>>();
await using var ctx = await factory.CreateDbContextAsync(ct);

var history = await ctx
    .PersistedOperationHistories
    .Where(h => h.Id == "userProfile_v1")
    .OrderByDescending(h => h.ChangedAt)
    .Take(10)
    .ToListAsync(ct);
```

## PersistedOperationHistory properties

| Property | Type | Column | Notes |
|---|---|---|---|
| `HistoryId` | `long` | `history_id` | Surrogate key (`bigserial`). |
| `TenantKey` | `string?` | `tenant_key` | Mirrors the live row. |
| `Id` | `string` | `id` | Mirrors the live row. |
| `Document` | `string` | `document` | Snapshot at the time of the change. |
| `ShapeFingerprint` | `string` | `shape_fingerprint` | Snapshot at the time of the change. |
| `ChangeType` | `string` | `change_type` | One of `Upsert`, `Deactivate`, `Restore`. |
| `ChangedAt` | `DateTime` | `changed_at` | UTC. |
| `ChangedReason` | `string?` | `changed_reason` | Required on deactivate. |
