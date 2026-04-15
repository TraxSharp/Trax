---
layout: default
title: ITraxAuditSink
parent: API Audit
grand_parent: SDK Reference
---

# ITraxAuditSink

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Destination for audit entries. Hosts implement once per deployment (Postgres, Serilog, CloudWatch, S3, whatever).

## Signature

```csharp
public interface ITraxAuditSink
{
    Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct);
}
```

## Guarantees

- Called from the background writer thread, never from the request thread.
- Batch size ranges from 1 to `TraxAuditOptions.BatchSize`.
- Exceptions are retried up to `MaxRetries` times with exponential backoff. After that the batch is dropped and logged.
- Writer swallows all failures. A crashed sink must not crash GraphQL requests.

## Example

Postgres sink with `IDbContextFactory`:

```csharp
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
