---
layout: default
title: UsePersistedOperationsEnforcement
parent: Persisted Operations
grand_parent: SDK Reference
---

# UsePersistedOperationsEnforcement

Kept so existing hosts compile. It adds nothing to the ASP.NET pipeline: enforcement runs inside HotChocolate's execution pipeline as soon as [`UsePersistedOperations`](/docs/sdk-reference/persisted-operations/use-persisted-operations) is configured, whether or not this is called. It is hidden from IntelliSense.

## Signature

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public static IApplicationBuilder UsePersistedOperationsEnforcement(
    this IApplicationBuilder app
);
```

Returns `app` unchanged. Throws `ArgumentNullException` when `app` is null.

## Where enforcement runs

A request middleware placed right after HotChocolate's document parser and before validation. Every transport reaches the executor through that pipeline: a JSON POST, a GET, a multipart POST, a WebSocket `subscribe`, and a request built in-process.

## Decision flow

For each operation:

1. If the document came from the store (the request named a persisted id): execute it.
2. If the request carries HotChocolate's `AllowNonPersistedOperation()` override, which only host code building its own request can set: execute it.
3. Otherwise the document is inline:
   - **Allowlist match** (operation name in `AllowOperations` or matching a predicate; the document id when there is no name): execute it. Both keys are chosen by the caller and the document is not inspected, so the allowlist is a convenience for trusted networks, not a control. See [Allowlist and dev carve-outs](/docs/persisted-operations#allowlist-and-dev-carve-outs).
   - **Management surface** (every operation selects `operations { persistedOperations { ... } }` and nothing else): execute it.
   - **Introspection** (the parsed document's top-level selections are all `__schema`, `__type` or `__typename`): execute it unless `DisableIntrospection()` was called.
   - **Enforcement off** (`RequirePersisted(false)`): execute it, logging at Information when `LogNonPersistedRequests(true)`.
   - **Otherwise**: refuse it. Over HTTP the status is `400 Bad Request` and the body is:

```json
{
  "errors": [{
    "message": "Only persisted operations are accepted on this server.",
    "extensions": { "code": "PERSISTED_OPERATION_REQUIRED" }
  }]
}
```

Over a socket the same error arrives as an `error` message for that operation id. Each entry of a batched request is decided on its own.

## Auth ordering

Enforcement runs inside HotChocolate, after ASP.NET authentication and after `TraxGraphQLAuthInterceptor` built the request, so an endpoint gated by `RequireAuthorization()` rejects an unauthenticated caller before any persisted-document lookup.

## SDK Reference

> [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)
