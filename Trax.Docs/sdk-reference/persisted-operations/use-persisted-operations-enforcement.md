---
layout: default
title: UsePersistedOperationsEnforcement
parent: Persisted Operations
grand_parent: SDK Reference
---

# UsePersistedOperationsEnforcement

Inserts the persisted-operations enforcement middleware into the ASP.NET pipeline. Reads each inbound POST request body, applies the configured policy, and either passes through or rejects with a typed GraphQL error.

## Signature

```csharp
public static IApplicationBuilder UsePersistedOperationsEnforcement(
    this IApplicationBuilder app
);
```

## Usage

Place AFTER `UseRouting()` and authentication, BEFORE `UseTraxGraphQL()`:

```csharp
var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UsePersistedOperationsEnforcement();
app.UseTraxGraphQL();
```

## Decision flow

For each `POST` to a JSON or GraphQL content-type endpoint:

1. If the body has only `id` / `documentId` (no inline `query`): pass through. HotChocolate looks up the document via `IOperationDocumentStorage`.
2. If the body has an inline `query`:
   - **Allowlist match** (operation name in `AllowOperations` or matching a predicate): pass through.
   - **Introspection** (operation name `IntrospectionQuery`, or AST is purely `__schema` / `__type`): pass through unless `DisableIntrospection()` was called.
   - **Shadow mode** (`LogNonPersistedRequests(true)`): log structured event at Information.
   - **Enforcement off** (`RequirePersisted(false)`): pass through.
   - **Otherwise**: respond `400 Bad Request` with body:

```json
{
  "errors": [{
    "message": "Only persisted operations are accepted on this server.",
    "extensions": { "code": "PERSISTED_OPERATION_REQUIRED" }
  }]
}
```

The middleware enables `Request.EnableBuffering()` so HotChocolate can re-read the body.

## Auth ordering

The middleware runs *after* ASP.NET authentication middleware, so `HttpContext.User` is populated by the time it inspects a request. The persisted-document lookup itself happens inside HotChocolate's pipeline AFTER `TraxGraphQLAuthInterceptor`, so unauthenticated requests are rejected before any DB lookup.
