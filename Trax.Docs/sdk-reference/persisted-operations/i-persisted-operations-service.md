---
title: IPersistedOperationsService
description: "Reference for IPersistedOperationsService, the management surface shared by the GraphQL fields and the dashboard, returning refusals as payload errors."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 6
package: Trax.Api.GraphQL.PersistedOperations
namespace:
  - Trax.Api.GraphQL.PersistedOperations.Services
  - Trax.Api.GraphQL.PersistedOperations.GraphQL.Models
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/IPersistedOperationsService.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/PersistedOperationsService.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/IPersistedOperationsManagementGate.cs
---

# IPersistedOperationsService

The persisted-operation management surface. The `persistedOperations` GraphQL fields call it, and the dashboard calls it, so an upload, deactivation or restore is accepted or refused the same way, with the same codes, from either.

Registered as a singleton by [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) and by [AddPersistedOperationStore](/docs/sdk-reference/persisted-operations/add-persisted-operation-store).

## Signature

```csharp fragment
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

## Members

The inputs, payloads and DTOs are sealed records in `Trax.Api.GraphQL.PersistedOperations.GraphQL.Models`, the same types the [management fields](/docs/sdk-reference/persisted-operations/management-mutations) take and return.

| Type | Fields |
|------|--------|
| `UploadPersistedOperationInput` | `Id`, `Document`, `Description`, `BypassShapeDiff` (default `false`), `Version` (default `0`), `TenantKey` |
| `DeactivatePersistedOperationInput` | `Id`, `Reason`, `TenantKey` |
| `RestorePersistedOperationInput` | `Id`, `TenantKey` |
| `UploadPersistedOperationPayload`, `DeactivatePersistedOperationPayload`, `RestorePersistedOperationPayload` | `Operation` (`PersistedOperationDto?`), `Errors` (`IReadOnlyList<PersistedOperationError>`), `Success` (true when `Operation` is set and `Errors` is empty) |
| `PersistedOperationFilter` | `IsActive`, `TenantKey`, `IdStartsWith`, all optional |
| `PersistedOperationsPage` | `Items` (`IReadOnlyList<PersistedOperationDto>`), `TotalCount` |
| `PersistedOperationDto` | The columns of [PersistedOperation](/docs/sdk-reference/persisted-operations/persisted-operation) |
| `PersistedOperationHistoryDto` | The columns of `PersistedOperationHistory` |
| `PersistedOperationError` | `Code`, `Message`, `Locations`, `Path`, `OldFingerprint`, `NewFingerprint`; see the [error payload](/docs/sdk-reference/persisted-operations/management-mutations#error-payload). `PersistedOperationError.NotFound(id)` builds the `NOT_FOUND` error |
| `PersistedOperationErrorLocation` | `Line`, `Column`, 1-based |

## Behavior

- The writes never throw for a refused change. The payload carries `Success = false` and `Errors`, each with a stable `Code`: `INVALID_INPUT`, `NOT_FOUND`, `PARSE_FAILED`, `SCHEMA_VALIDATION_FAILED` or `SHAPE_DIFF_VIOLATION`. The codes are the ones in the [management mutations' error payload](/docs/sdk-reference/persisted-operations/management-mutations#error-payload).
- A change that was saved but whose broadcast the broker did not confirm comes back with `Success = false`, the saved `Operation`, and one `CHANGE_NOT_BROADCAST` error. It is in force on this node; other nodes pick it up within the cache's maximum age. Repeating the change sends it again.
- `UploadAsync` goes through [IPersistedOperationStore.UpsertAsync](/docs/sdk-reference/persisted-operations/i-persisted-operation-store): schema validation, exactly one operation per document, the shape-diff guardrail, history, cache invalidation and broadcast.
- `DeactivateAsync` requires a reason. An unknown id is `NOT_FOUND`. Deactivating an operation that is already deactivated succeeds, records the new reason, and sends the change to every node again.
- `RestoreAsync` acts on any existing operation, active or deactivated.
- `GetAsync` returns active and deactivated operations, and null when there is none.
- `ListAsync` returns the most recently updated first. `GetHistoryAsync` returns the most recent change first. For both, `take` outside 1 to 200 reads as 50 and a negative `skip` reads as 0.

## IPersistedOperationsManagementGate

The management gate the host declared with `GateManagement`, for a surface that calls the service on a person's behalf without GraphQL. The Trax dashboard asks it before every read and write on its persisted-operations pages, so it refuses the people the `persistedOperations` fields refuse. Registered by `UsePersistedOperations` and `AddPersistedOperationStore`, scoped: resolve it from the caller's scope (the request, or the dashboard's circuit), so a policy whose handler is scoped is evaluated there. The service itself does not ask it: a deploy step or a migration calls the service with no person behind it.

```csharp fragment
public interface IPersistedOperationsManagementGate
{
    bool IsDeclared { get; }
    Task<bool> AllowsAsync(ClaimsPrincipal user, CancellationToken ct = default);
}
```

| Member | Description |
|--------|-------------|
| `IsDeclared` | True once the host declared a gate. When false no surface manages documents |
| `AllowsAsync(user)` | True when any identity of `user` is authenticated, satisfies every policy and holds at least one role of each role list. False when no gate is declared, and when a policy is not registered or the host registers no authorization |

## Example

```csharp compile
var service = serviceProvider.GetRequiredService<IPersistedOperationsService>();

var result = await service.DeactivateAsync(
    new DeactivatePersistedOperationInput("userProfile_v1", Reason: "replaced by userProfile_v2"),
    cancellationToken
);

if (!result.Success)
    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
```

## See also

- [Management mutations and queries](/docs/sdk-reference/persisted-operations/management-mutations)
- [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store)
