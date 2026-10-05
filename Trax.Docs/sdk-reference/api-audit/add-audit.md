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
using Trax.Api.GraphQL.Audit;       // AddAudit, ITraxAuditSink, TraxAuditEntry, TraxAuditOptions
using Trax.Api.GraphQL.Extensions;  // AddTraxGraphQL

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
| `ITraxAuditSink` -> `TSink` | Scoped | Consumer-provided destination. Its constructor dependencies resolve per batch, so an `IDbContextFactory<T>` or a scoped `DbContext` both work. |
| `ITraxAuditRedactor` -> `DefaultAuditRedactor` | Singleton (TryAdd) | Records no variables. Register your own to record them. |
| `IHttpContextAccessor` | Singleton | Listener reads `HttpContext.User`. |
| Disclaimer hosted service | Singleton (idempotent) | One-shot startup NO-WARRANTY log. |
| `IValidateOptions<TraxAuditOptions>` | Singleton, with `ValidateOnStart` | Refuses startup when an option is out of range, naming it. |

See [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options) for every knob.

Every GraphQL request is recorded, including one a gate refuses: a refused train, query model or `operations` call is an entry with `Success = false`, `ErrorText = "Not authorized."` and the caller's scheme-qualified `PrincipalId`. The [Auth sample](/docs/samples/auth) stores entries in a table and serves them to auditors as a gated query model.

## Package

```
dotnet add package Trax.Api.GraphQL.Audit
```
