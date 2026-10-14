---
title: TraxAuditOptions
description: Reference for TraxAuditOptions, the options of the GraphQL audit pipeline such as ChannelCapacity, BatchSize and FlushInterval, their defaults and validation.
type: reference
area: security
parent: API Audit
grand_parent: SDK Reference
nav_order: 5
package: Trax.Api.GraphQL.Audit
namespace: Trax.Api.GraphQL.Audit
source:
  - Trax.Api/src/Trax.Api.GraphQL.Audit/TraxAuditOptions.cs
  - Trax.Api/src/Trax.Api.GraphQL.Audit/TraxAuditOptionsValidator.cs
---

# TraxAuditOptions

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

The options of the audit pipeline, set through `AddAudit<TSink>(opts => ...)`.

## Signature

```csharp fragment
public sealed class TraxAuditOptions
```

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ChannelCapacity` | `int` | `10_000` | Most entries the channel holds. When it is full a new entry is dropped and counted in `trax.audit.dropped`. |
| `BatchSize` | `int` | `50` | Most entries handed to the sink in one call. |
| `FlushInterval` | `TimeSpan` | 500 ms | Longest the writer waits for a batch to fill before it writes a partial one. |
| `MaxDocumentLength` | `int` | `65_536` | A longer document is cut to this length, marked `...[truncated]`, and followed by `[selected fields: ...]`, every `Type.field` the operation selects. The list names each schema coordinate once, so its size is bounded by the schema rather than by the request. |
| `MaxOperationNameLength` | `int` | `256` | A longer operation name is cut to this length and marked `...[truncated]`. The name is whatever the caller sent. |
| `MaxErrorTextLength` | `int` | `4_096` | A longer `ErrorText` is cut to this length and marked `...[truncated]`. A request can raise one error per field it selects. |
| `RecordErrorMessages` | `bool` | `false` | Records error messages in `ErrorText`. Off, each error is recorded as `CODE at path` (`<masked>` when it has no code) and a pipeline exception as its type name, because a message can quote what the caller sent. |
| `SkipIntrospection` | `bool` | `true` | Drops introspection operations that succeeded: every top-level selection of the executed operation is `__schema`, `__type` or `__typename`. One that raised an error is recorded. |
| `SkipSubscriptions` | `bool` | `true` | Drops subscriptions that were accepted (returned a stream). A subscription refused or failed when it subscribes is always recorded, and so is each failed event of an accepted one and each socket refused at `connection_init`. |
| `DefaultPrincipalId` | `string` | `"<anonymous>"` | The `PrincipalId` of an entry whose request has no `trax:principal-id` claim. |
| `MaxRetries` | `int` | `3` | Retries a failing sink gets after its first attempt before the batch is dropped, so the default makes 4 attempts in all. |
| `RetryBackoff` | `TimeSpan` | 100 ms | The first wait between sink retries. It doubles on each attempt up to `MaxRetryBackoff`, and each wait is a random point between half and all of that value. |
| `MaxRetryBackoff` | `TimeSpan` | 30 s | The longest wait between two sink retries, whatever the attempt. |

## Behavior

`AddAudit` validates the options when the host starts. The host refuses to start, with an `OptionsValidationException` naming each option out of range, when:

- `ChannelCapacity`, `BatchSize`, `MaxDocumentLength`, `MaxOperationNameLength` or `MaxErrorTextLength` is 0 or less;
- `FlushInterval` is zero or less;
- `MaxRetries` is below 0 or above 100;
- `MaxRetryBackoff` is zero or less, or above one hour;
- `RetryBackoff` is negative or above `MaxRetryBackoff`;
- `DefaultPrincipalId` is empty.

Each failure reads `TraxAuditOptions.<Option> <rule>; it is <value>. Set it in AddAudit(options => ...).`

The writer is single-threaded: while it retries one batch, the channel fills behind it.

## Example

```csharp compile
services.AddTraxGraphQL(graphql =>
    graphql.AddAudit<AuditTableSink>(opts =>
    {
        opts.ChannelCapacity = 50_000;
        opts.BatchSize = 200;
        opts.FlushInterval = TimeSpan.FromSeconds(2);
        opts.MaxRetries = 15;
    })
);
```

## See also

- [Tune the audit pipeline](/docs/security/tune-the-audit-pipeline)
- [Drops and shutdown](/docs/security/tune-the-audit-pipeline#watch-for-lost-entries)
- [AddAudit](/docs/sdk-reference/api-audit/add-audit), [TraxAuditChannel](/docs/sdk-reference/api-audit/trax-audit-channel)
