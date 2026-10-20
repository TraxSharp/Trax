---
title: AddAudit
description: "Reference for AddAudit, which wires the GraphQL audit pipeline on TraxGraphQLBuilder: channel, background writer, diagnostic listener, sink and redactor."
type: reference
area: security
parent: API Audit
grand_parent: SDK Reference
nav_order: 1
package: Trax.Api.GraphQL.Audit
namespace: Trax.Api.GraphQL.Audit
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/TraxGraphQLBuilderAuditExtensions.cs
---

# AddAudit

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/security/secure-a-graphql-host).

Extension method on `TraxGraphQLBuilder`, declared on `TraxGraphQLBuilderAuditExtensions`, that registers the GraphQL audit pipeline: a bounded channel, a background writer, a HotChocolate diagnostic listener, the sink `TSink` and a redactor.

## Signature

```csharp fragment
public static TraxGraphQLBuilder AddAudit<TSink>(
    this TraxGraphQLBuilder builder,
    Action<TraxAuditOptions>? configure = null
)
    where TSink : class, ITraxAuditSink;
```

## Parameters

| Parameter | Type | Description |
|---|---|---|
| `TSink` | type argument | The [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink) that receives the entries. |
| `builder` | `TraxGraphQLBuilder` | The builder `AddTraxGraphQL` passes to its callback. |
| `configure` | `Action<TraxAuditOptions>?` | Sets [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options). Omitted, every option keeps its default. |

## Returns

The same `TraxGraphQLBuilder`, for chaining.

## Behavior

Every GraphQL request is recorded as a [TraxAuditEntry](/docs/sdk-reference/api-audit/trax-audit-entry), including one a gate refuses: a refused train, query model or `operations` call is an entry with `Success = false` and the caller's scheme-qualified `PrincipalId`. A socket refused at `connection_init` and a failed event of an accepted subscription are recorded too. The request thread never waits on the sink.

The options are validated when the host starts. A value out of range refuses startup with an `OptionsValidationException` naming the option; the rules are on [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options#behavior).

Throws `ArgumentNullException` when `builder` is null.

### Registered services

| Service | Lifetime | Purpose |
|---|---|---|
| [`TraxAuditChannel`](/docs/sdk-reference/api-audit/trax-audit-channel) | Singleton | Bounded channel between the listener and the writer. Counts dropped entries. |
| `TraxGraphQLAuditListener` | Singleton, added to the schema as a diagnostic event listener | Builds an entry for each request and offers it to the channel. |
| `TraxAuditWriter` | Hosted service | Drains the channel in batches and calls the sink, with retries. On shutdown it writes every accepted entry within the host's shutdown timeout. |
| `ITraxAuditSink` -> `TSink` | Scoped | The destination. It is resolved from a new scope for each write attempt, so an `IDbContextFactory<T>` or a scoped `DbContext` both work. |
| `ITraxAuditRedactor` -> `DefaultAuditRedactor` | Singleton, only when none is registered | Records no variables. Register your own [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor) to record them. |
| `ISocketRefusalObserver` -> `TraxSocketRefusalAudit` | Singleton | Records a socket refused at `connection_init`, which never reaches the listener. |
| `IHttpContextAccessor` | Singleton | The listener reads `HttpContext.User`. |
| `TimeProvider` | Singleton, only when none is registered | `TimeProvider.System`. |
| `IValidateOptions<TraxAuditOptions>` | Singleton, with `ValidateOnStart` | Refuses startup when an option is out of range. |
| Disclaimer hosted service | Singleton, registered once | Logs the NO WARRANTY notice at startup. |

## Example

```csharp compile
services.AddTraxGraphQL(graphql =>
    graphql.AddAudit<AuditTableSink>(opts =>
    {
        opts.ChannelCapacity = 10_000;
        opts.BatchSize = 50;
        opts.FlushInterval = TimeSpan.FromMilliseconds(500);
    })
);
```

## See also

- [Audit pipeline](/docs/security/secure-a-graphql-host#3-write-the-audit-sink): what an entry records, drops and shutdown.
- [Tune the audit pipeline](/docs/security/tune-the-audit-pipeline)
- [TraxAuditOptions](/docs/sdk-reference/api-audit/trax-audit-options), [ITraxAuditSink](/docs/sdk-reference/api-audit/i-trax-audit-sink), [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor)
- The [Auth sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/Auth) stores entries in a table and serves them to auditors as a gated query model.
