---
title: Tune the audit pipeline
description: How to size the GraphQL audit channel, batches and retries for a host's traffic and sink, and what to watch so lost entries do not go unnoticed.
type: how-to
area: security
nav_order: 20
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/TraxAuditOptions.cs
---

# Tune the audit pipeline

The defaults suit a host with modest traffic and a sink that answers in milliseconds. Change them
when the traffic or the sink is not like that. Every option, its default and its valid range is on
[TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options).

## Size the channel and batches

1. For a high-traffic host, raise `ChannelCapacity` so a burst fits while the writer catches up,
   and keep `BatchSize` modest (50 to 100). Set `FlushInterval` close to the sink's write latency.
2. For a sink with a high cost per call (a relational database, object storage), raise `BatchSize`
   to 200 or more and lengthen `FlushInterval` to match, so each write carries more entries.

```csharp compile
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;

services.AddTraxGraphQL(graphql =>
    graphql.AddAudit<AuditTableSink>(opts =>
    {
        opts.ChannelCapacity = 50_000;
        opts.BatchSize = 200;
        opts.FlushInterval = TimeSpan.FromSeconds(2);
    })
);

public sealed class AuditTableSink : ITraxAuditSink
{
    public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct) =>
        Task.CompletedTask;
}
```

## Ride out sink outages

1. Where every entry has to be kept, raise `MaxRetries` so a short outage does not drop a batch.
   With the 30 second `MaxRetryBackoff` cap, 15 retries hold one batch for at most about four
   minutes.
2. Raise `ChannelCapacity` with it. The writer is single-threaded, so the channel fills while it
   retries, and a full channel drops new entries instead.

## Watch for lost entries

Alert on any non-zero `trax.audit.dropped` from the `Trax.Audit` meter
([TraxAuditChannel](/docs/sdk-reference/api-audit/trax-audit-channel)). It counts every lost entry:
one the listener failed to capture or build, one refused by a full channel, one refused by the sink
after every retry, and one unwritten when shutdown ran out of time.

## Give shutdown enough time

The writer drains every accepted entry on graceful shutdown, within `HostOptions.ShutdownTimeout`
(30 seconds by default). If the sink needs longer to write a full channel, raise the timeout:

```csharp compile
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMinutes(2));
```

## SDK Reference

> [AddAudit](/docs/sdk-reference/api-audit/add-audit) | [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options) | [TraxAuditChannel](/docs/sdk-reference/api-audit/trax-audit-channel) | [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink)
