---
title: TraxAuditChannel
description: Reference for TraxAuditChannel, the bounded queue between the audit listener and writer, its Trax.Audit meter and the trax.audit.dropped counter.
type: reference
area: security
parent: API Audit
grand_parent: SDK Reference
nav_order: 6
package: Trax.Api.GraphQL.Audit
namespace: Trax.Api.GraphQL.Audit
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/TraxAuditChannel.cs
---

# TraxAuditChannel

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

The bounded queue between the audit listener, which offers entries, and the background writer, which drains them. `AddAudit` registers it as a singleton. It owns the `Trax.Audit` meter and its `trax.audit.dropped` counter.

## Signature

```csharp fragment
public sealed class TraxAuditChannel : IDisposable
{
    public const string MeterName = "Trax.Audit";
    public const string DroppedCounterName = "trax.audit.dropped";

    public TraxAuditChannel(IOptions<TraxAuditOptions> options, ILogger<TraxAuditChannel> logger);

    public long TotalDropped { get; }
}
```

## Members

| Member | Value |
|---|---|
| `MeterName` | `Trax.Audit`, the `System.Diagnostics.Metrics` meter the counter belongs to. |
| `DroppedCounterName` | `trax.audit.dropped`, a `Counter<long>` with unit `{entry}`. |
| `TotalDropped` | Entries dropped since the process started, the same count the counter reports. |

## Behavior

The channel holds up to `TraxAuditOptions.ChannelCapacity` entries and never blocks the listener: an entry offered while it is full, or after the writer has stopped accepting entries for shutdown, is refused and counted as dropped.

`trax.audit.dropped` counts each entry that will never reach the sink, once:

- the listener failed to capture or build it;
- the channel was full, or shutting down;
- the sink refused its batch after every retry;
- shutdown ran out of time before it was written.

A drop logs a warning with the running total, at most once every five seconds.

## Example

Export the meter with OpenTelemetry, and alert on a non-zero `trax.audit.dropped`:

```csharp fragment
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(TraxAuditChannel.MeterName));
```

## See also

- [Drops and shutdown](/docs/security/tune-the-audit-pipeline#watch-for-lost-entries)
- [AddAudit](/docs/sdk-reference/api-audit/add-audit), [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink), [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options)
