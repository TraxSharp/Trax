---
layout: default
title: AddAudit
description: "Reference for AddAudit, which wires the GraphQL audit pipeline on TraxGraphQLBuilder: channel, background writer, diagnostic listener, sink and redactor."
parent: API Audit
grand_parent: SDK Reference
---

# AddAudit

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Fluent extension on `TraxGraphQLBuilder` that wires the complete audit pipeline: bounded channel, background writer, HotChocolate diagnostic listener, sink, and redactor.

## Signature

```csharp
public static TraxGraphQLBuilder AddAudit<TSink>(
    this TraxGraphQLBuilder builder,
    Action<TraxAuditOptions>? configure = null
)
    where TSink : class, ITraxAuditSink;
```

## Usage

```csharp
services.AddTraxGraphQL(graphql =>
    graphql.AddAudit<MyPostgresAuditSink>(opts =>
    {
        opts.ChannelCapacity = 10_000;
        opts.BatchSize = 50;
        opts.FlushInterval = TimeSpan.FromMilliseconds(500);
    })
);
```

## What Gets Registered

| Service | Lifetime | Purpose |
|---|---|---|
| `TraxAuditChannel` | Singleton | Bounded channel between listener and writer. |
| `TraxGraphQLAuditListener` | Singleton | HotChocolate `ExecutionDiagnosticEventListener`. |
| `TraxAuditWriter` | Hosted service (singleton) | Drains channel, batches, calls sink with retry; on shutdown writes every accepted entry within the host's shutdown timeout. |
| `ITraxAuditSink` -> `TSink` | Scoped | Consumer-provided destination. |
| `ITraxAuditRedactor` -> `DefaultAuditRedactor` | Singleton (TryAdd) | Records no variables. Register your own to record them. |
| `IHttpContextAccessor` | Singleton | Listener reads `HttpContext.User`. |
| Disclaimer hosted service | Singleton (idempotent) | One-shot startup NO-WARRANTY log. |

See [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options) for every knob.
