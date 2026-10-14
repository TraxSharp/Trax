---
title: IPersistedOperationBroadcaster
description: "Reference for IPersistedOperationBroadcaster, PersistedOperationChangedMessage and PersistedOperationChangeType, which tell other nodes of a change."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 10
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Broadcasting
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Broadcasting/IPersistedOperationBroadcaster.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Broadcasting/PersistedOperationChangedMessage.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Broadcasting/RabbitMqPersistedOperationBroadcaster.cs
---

# IPersistedOperationBroadcaster

Publishes a `PersistedOperationChangedMessage` after the store saves a change, so the other nodes
empty their persisted-operation caches.

## Signature

```csharp fragment
public interface IPersistedOperationBroadcaster
{
    Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct);
}

public sealed record PersistedOperationChangedMessage(
    string? TenantKey,
    string Id,
    string ChangeType,
    DateTime Timestamp);

public static class PersistedOperationChangeType
{
    public const string Upsert = "Upsert";
    public const string Deactivate = "Deactivate";
    public const string Restore = "Restore";
}
```

## Members

| Member | Description |
|--------|-------------|
| `PublishAsync(message, ct)` | Publishes the message and completes once the transport has taken responsibility for it; for RabbitMQ, the broker's publisher confirm. Throws when it could not |
| `TenantKey`, `Id` | The operation that changed. Serialized as `tenantKey` and `id` |
| `ChangeType` | One of the `PersistedOperationChangeType` values: `Upsert` (inserted or rewritten), `Deactivate`, `Restore`. Serialized as `changeType` |
| `Timestamp` | When the change was made. Serialized as `timestamp` |

## Behavior

- `UseRabbitMqInvalidation(...)` registers the RabbitMQ implementation, which publishes to a fanout
  exchange every node subscribes to. `SingleNode()` registers a no-op.
- When `PublishAsync` throws, the store has already saved the change and reports it as
  `PersistedOperationNotBroadcastException` (`CHANGE_NOT_BROADCAST`).
- A node receiving a message empties its document, prepared-operation and lookup caches.

## Example

```csharp compile
var message = new PersistedOperationChangedMessage(
    TenantKey: null,
    Id: "userProfile_v1",
    ChangeType: PersistedOperationChangeType.Deactivate,
    Timestamp: DateTime.UtcNow
);

await serviceProvider.GetRequiredService<IPersistedOperationBroadcaster>().PublishAsync(message, ct);
```

## See also

- [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
- [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions)
- [One node or many](/docs/persisted-operations#one-node-or-many)
