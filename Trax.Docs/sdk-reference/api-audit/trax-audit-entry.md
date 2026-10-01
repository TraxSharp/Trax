---
layout: default
title: TraxAuditEntry
parent: API Audit
grand_parent: SDK Reference
---

# TraxAuditEntry

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Immutable record describing one GraphQL request, including one the endpoint policy refused. Built by the listener and passed in batches to `ITraxAuditSink.WriteAsync`.

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
| `OperationName` | The GraphQL operation name, if any. |
| `Document` | The GraphQL document with every string literal replaced by `""` and every numeric literal by `0`; field names, aliases, input field names, booleans, enum values and `null` are kept. Past `TraxAuditOptions.MaxDocumentLength` it is cut to that length, marked `...[truncated]`, and followed by `[selected fields: Type.field, ...]`: every field the compiled operation selects, after fragment expansion, each schema coordinate listed once. |
| `Variables` | What the registered [ITraxAuditRedactor](/docs/sdk-reference/api-audit/i-trax-audit-redactor) returned, as a `JsonObject`. `null` by default: the default redactor records no variables. |
| `DurationMs` | Elapsed request time in milliseconds. |
| `Timestamp` | UTC when the request started, NOT when the entry was persisted. |
| `Success` | False on exception, GraphQL errors in the result, or a refusal by the endpoint policy. |
| `ErrorText` | Joined error messages or the exception message, as written; not redacted. `Not authorized.` for a request the endpoint policy refused. |
| `Metadata` | Bag for host-provided extras (request IP, tenant ID, correlation ID). Not populated by the default listener. |
