---
title: ITraxAuditSink
description: Reference for ITraxAuditSink, the destination you implement for audit entries, and how the writer calls it on batching, threading, retries and drops.
type: reference
area: security
parent: API Audit
grand_parent: SDK Reference
nav_order: 3
package: Trax.Api.GraphQL.Audit
namespace: Trax.Api.GraphQL.Audit
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/ITraxAuditSink.cs
---

# ITraxAuditSink

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The destination for audit entries, implemented by the host (a database table, a log pipeline, object storage) and registered with `AddAudit<TSink>()`.

## Signature

```csharp fragment
public interface ITraxAuditSink
{
    Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct);
}
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `batch` | `IReadOnlyList<TraxAuditEntry>` | From 1 to `TraxAuditOptions.BatchSize` [entries](/docs/sdk-reference/api-audit/trax-audit-entry). |
| `ct` | `CancellationToken` | Fires when the host's shutdown timeout runs out, not when shutdown begins. |

## Returns

A task that completes when the batch is stored. A faulted task counts as a failed attempt.

## Behavior

- Called from the background writer, never from a request thread. The writer is single-threaded: one call at a time.
- The sink is resolved from a new scope for each attempt.
- A failed attempt is retried up to `MaxRetries` times, waiting `RetryBackoff` doubled on each attempt, capped at `MaxRetryBackoff`, and jittered between half and all of that value. After the last retry the batch is dropped, logged, and counted in `trax.audit.dropped`.
- On graceful shutdown the writer keeps writing accepted entries until the host's shutdown timeout, and `ct` fires then. Whatever is unwritten at that point is counted as dropped.
- The writer catches every sink failure. A failing sink does not fail a GraphQL request.

## Example

A sink that writes each batch through an `IDbContextFactory`:

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Audit;

public sealed class PostgresAuditSink(IDbContextFactory<AppDbContext> factory) : ITraxAuditSink
{
    public async Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AuditEntries.AddRange(batch.Select(e => new AuditRow(e)));
        await db.SaveChangesAsync(ct);
    }
}
```

## See also

- [Drops and shutdown](/docs/security/tune-the-audit-pipeline#watch-for-lost-entries)
- [Tune the audit pipeline](/docs/security/tune-the-audit-pipeline)
- [AddAudit](/docs/sdk-reference/api-audit/add-audit), [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options), [TraxAuditChannel](/docs/sdk-reference/api-audit/trax-audit-channel)
