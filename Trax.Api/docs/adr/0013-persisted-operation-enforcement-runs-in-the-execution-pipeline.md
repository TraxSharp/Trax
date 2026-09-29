---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# Persisted-operation enforcement runs in the execution pipeline

`UsePersistedOperations` enforces its policy in a HotChocolate request middleware that runs
right after the document is parsed and before it is validated. Every way a document reaches the
executor passes through that pipeline: a JSON POST, a GET, a multipart POST, a WebSocket
`subscribe`, and a request host code builds in-process. They all get one decision, taken from
the operation name, the document id and the parsed document, never from the transport's
framing.

## Status

**Accepted.**

## What the decision is

- A document the store supplied (`IsPersisted`) executes.
- An inline document executes when it is allowlisted by name (or by document id when it has no
  name), selects only the persisted-operations management surface, or selects only
  introspection fields and `DisableIntrospection()` was not called. Introspection is judged
  from the document; a name plays no part.
- Otherwise, with `RequirePersisted(true)`, it is refused with `PERSISTED_OPERATION_REQUIRED`
  and HTTP status 400, or an `error` message on a socket.

`UsePersistedOperationsEnforcement()` is kept so existing hosts compile, and adds nothing to the
ASP.NET pipeline. It is hidden from IntelliSense rather than removed, the same answer core/0002
gives for a shipped seam: a public member that published hosts call stays public.

## Considered options

**HotChocolate's own `OnlyAllowPersistedDocuments`, with Trax granting
`AllowNonPersistedOperation` for the carve-outs.** It is the stock switch, but it runs before the
document is parsed, so every carve-out would have to parse the document a second time, and it
has no shadow mode. One middleware after the parser does both with the document HotChocolate
already built.

**Keep the ASP.NET middleware and teach it the other transports.** It would have to parse query
strings, multipart forms and socket frames itself, and a socket frame never reaches ASP.NET
middleware at all. Each new transport would be a new gap.

**Exempt in-process requests.** Host code is trusted, and a request with no `HttpContext` is
almost always host code. Rejected because absence of a feature is not a signal worth trusting,
and HotChocolate already has an explicit, named override for exactly this:
`AllowNonPersistedOperation()` on the request builder. The middleware honours it; no Trax
transport sets it.

## Consequences

**Enforcement no longer depends on a middleware call.** A host that configured
`UsePersistedOperations` with enforcement on but never called `UsePersistedOperationsEnforcement()`
is now enforced, which is what the startup log line always said.

**Each entry of a batched request is decided on its own.** The ASP.NET middleware refused the
whole batch when any entry was refused. Batching is off in HotChocolate by default.

## Exemplars

- `PersistedOperationTransportTests` runs the decision over a real `TestServer` for JSON POST,
  GET, multipart, WebSocket and in-process requests, with the carve-outs on each.
- `PersistedOperationPolicyTests.cs` covers the decision case by case, without a transport.
- [Persisted Operations](/docs/persisted-operations) is the rule this produces.

Not covered: nothing checks that a new transport HotChocolate adds routes through the request
pipeline; every transport it ships today does.

## Changelog

- **2026-09-27**: Recorded.
