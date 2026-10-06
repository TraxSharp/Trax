---
layout: default
title: IPersistedOperationsService
description: Reference for IPersistedOperationsService, the management surface shared by the GraphQL fields and the dashboard, returning refusals as payload errors.
parent: Persisted Operations
grand_parent: SDK Reference
---

# IPersistedOperationsService

The persisted-operation management surface. The `operations.persistedOperations` GraphQL fields call it, and the dashboard calls it, so an upload, deactivation or restore is accepted or refused the same way, with the same codes, from either. Use it from admin tooling that should behave exactly as the API does.

Registered as a singleton by [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) and by `AddPersistedOperationStore`.

## Interface

```csharp
public interface IPersistedOperationsService
{
    Task<PersistedOperationsPage> ListAsync(PersistedOperationFilter? filter, int skip, int take, CancellationToken ct);
    Task<PersistedOperationDto?> GetAsync(string id, string? tenantKey, CancellationToken ct);
    Task<IReadOnlyList<PersistedOperationHistoryDto>> GetHistoryAsync(string id, string? tenantKey, int skip, int take, CancellationToken ct);
    Task<UploadPersistedOperationPayload> UploadAsync(UploadPersistedOperationInput input, CancellationToken ct);
    Task<DeactivatePersistedOperationPayload> DeactivateAsync(DeactivatePersistedOperationInput input, CancellationToken ct);
    Task<RestorePersistedOperationPayload> RestoreAsync(RestorePersistedOperationInput input, CancellationToken ct);
}
```

## Behavior

- The writes never throw for a refused change. The payload carries `Success = false` and `Errors`, each with a stable `Code`: `INVALID_INPUT`, `NOT_FOUND`, `PARSE_FAILED`, `SCHEMA_VALIDATION_FAILED` or `SHAPE_DIFF_VIOLATION`. The codes are the ones in the [management mutations' error payload](/docs/sdk-reference/persisted-operations/management-mutations#error-payload).
- A change that was saved but whose broadcast the broker did not confirm comes back with `Success = false`, the saved `Operation`, and one `CHANGE_NOT_BROADCAST` error. It is in force on this node; other nodes pick it up within the cache's maximum age. Repeating the change sends it again.
- `UploadAsync` goes through [IPersistedOperationStore.UpsertAsync](/docs/sdk-reference/persisted-operations/i-persisted-operation-store): schema validation, exactly one operation per document, the shape-diff guardrail, history, cache invalidation and broadcast.
- `DeactivateAsync` requires a reason. An unknown id is `NOT_FOUND`. Deactivating an operation that is already deactivated succeeds, records the new reason, and sends the change to every node again.
- `RestoreAsync` acts on any existing operation, active or deactivated.
- `GetAsync` returns active and deactivated operations, and null when there is none.
- `ListAsync` returns the most recently updated first. `GetHistoryAsync` returns the most recent change first. For both, `take` outside 1 to 200 reads as 50 and a negative `skip` reads as 0.

## Example

```csharp
var service = serviceProvider.GetRequiredService<IPersistedOperationsService>();

var result = await service.DeactivateAsync(
    new DeactivatePersistedOperationInput("userProfile_v1", Reason: "replaced by userProfile_v2"),
    cancellationToken
);

if (!result.Success)
    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
```
