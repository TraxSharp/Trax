---
layout: default
title: TraxAuditEntry
description: Reference for TraxAuditEntry, the immutable record describing one audited GraphQL request, including refused ones, and each of its fields.
parent: API Audit
grand_parent: SDK Reference
---

# TraxAuditEntry

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Immutable record describing one GraphQL request, including one the endpoint policy refused and a subscription refused when it subscribed. Two more kinds of entry describe something other than a whole request: a socket connection refused at `connection_init`, and one failed event of an accepted subscription (see [Entries that are not a whole request](#entries-that-are-not-a-whole-request)). Built by the listener and passed in batches to `ITraxAuditSink.WriteAsync`.

## Signature

```csharp
public sealed record TraxAuditEntry(
    string PrincipalId,
    string? PrincipalType,
    string? OperationName,
    string Document,
    JsonObject? Variables,
    long DurationMs,
    DateTimeOffset Timestamp,
    bool Success,
    string? ErrorText,
    IReadOnlyDictionary<string, string>? Metadata = null
);
```

## Fields

| Field | Notes |
|---|---|
| `PrincipalId` | From `trax:principal-id` claim (qualified by scheme, `{scheme}:{id}`), or `TraxAuditOptions.DefaultPrincipalId` when absent. |
| `PrincipalType` | From `trax:principal-type` claim. `apikey`, `jwt`, or similar. `null` for anonymous. |
| `OperationName` | The request's `operationName` field, or, when the client sends none, the name the document gives its only operation (`query Feed { ... }` is recorded as `Feed`). `null` for an anonymous operation, or for a document with several operations and no `operationName`. Cut at `TraxAuditOptions.MaxOperationNameLength` and marked `...[truncated]`. |
| `Document` | The GraphQL document with every string literal replaced by `""` and every numeric literal by `0`, directive arguments included; field names, aliases, input field names, booleans, enum values and `null` are kept. Past `TraxAuditOptions.MaxDocumentLength` it is cut to that length, marked `...[truncated]`, and followed by `[selected fields: Type.field, ...]`: every field the compiled operation selects, after fragment expansion, each schema coordinate listed once. |
| `Variables` | What the registered [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor) returned, as a `JsonObject`. `null` by default: the default redactor records no variables. |
| `DurationMs` | Elapsed request time in milliseconds. |
| `Timestamp` | UTC when the request started, NOT when the entry was persisted. |
| `Success` | False on exception, GraphQL errors in the result, or a refusal by the endpoint policy or a subscription's authorization. |
| `ErrorText` | By default each error's code and path, `CODE at path`, joined with `; `: `<masked>` for an error with no code, the exception's type name for an exception the pipeline raised, `TRAX_AUTHORIZATION` for a request the endpoint policy refused. With `TraxAuditOptions.RecordErrorMessages` the messages as written instead. Cut at `MaxErrorTextLength`. |
| `Metadata` | Bag for host-provided extras (request IP, tenant ID, correlation ID). `null` on a request's entry; set on the two entries below, which a sink that stores only the other fields can still tell apart by `ErrorText`. |

## Entries that are not a whole request

| Entry | `ErrorText` | `Metadata` | Other fields |
|---|---|---|---|
| A socket refused at `connection_init` | `TRAX_SOCKET_REFUSED at connection_init (reason)` | `transport` = `websocket`, `stage` = `connection_init`, `scheme` = `ApiKey`, `Jwt` or `None`, `reason` = `missing credential`, `credential rejected` or `endpoint policy` | `Document` is empty and `OperationName` `null`: no operation was sent. `PrincipalId` is `DefaultPrincipalId`, or the connection's principal when its credential was accepted and the endpoint policy refused it. The credential is never recorded. |
| An error in an event of an accepted subscription | As for a request: `CODE at path` for each error the event raised, joined with `; `, or the exception's type when the event failed outside a resolver. Cut at `MaxErrorTextLength` | `subscriptionEvent` = `error`, `subscriptionEventErrors` = how many errors the event raised | One entry per failing event, however many of its fields failed, recorded whatever `SkipSubscriptions` says. `Document` and `OperationName` are the subscription's, `PrincipalId` the subscriber's, `DurationMs` 0 and `Timestamp` when the event failed. |

A host that replaces Trax's socket interceptor through `ConfigureSchema` gets no entry for the connections its interceptor refuses.
