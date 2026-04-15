---
layout: default
title: TraxAuditEntry
parent: API Audit
grand_parent: SDK Reference
---

# TraxAuditEntry

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

Immutable record describing one executed GraphQL request. Built by the listener and passed in batches to `ITraxAuditSink.WriteAsync`.

## Signature

```csharp
public sealed record TraxAuditEntry(
    string PrincipalId,
    string? PrincipalType,
    string? OperationName,
    string Document,
    IReadOnlyDictionary<string, object?>? Variables,
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
| `PrincipalId` | From `trax:principal-id` claim, or `TraxAuditOptions.DefaultPrincipalId` when absent. |
| `PrincipalType` | From `trax:principal-type` claim. `apikey`, `jwt`, or similar. `null` for anonymous. |
| `OperationName` | The GraphQL operation name, if any. |
| `Document` | The GraphQL query text. Truncated at `TraxAuditOptions.MaxDocumentLength` with a trailing `...[truncated]` marker. |
| `Variables` | Post-redaction. `null` when there were no variables or the redactor returned `null`. |
| `DurationMs` | Elapsed request time in milliseconds. |
| `Timestamp` | UTC when the request started, NOT when the entry was persisted. |
| `Success` | False on exception or GraphQL errors in the result. |
| `ErrorText` | Joined error messages or the exception message. |
| `Metadata` | Bag for host-provided extras (request IP, tenant ID, correlation ID). Not populated by the default listener. |
