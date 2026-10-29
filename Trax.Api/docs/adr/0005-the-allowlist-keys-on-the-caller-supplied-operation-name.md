---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# The persisted-operations allowlist keys on the caller-supplied operation name

`AllowOperations` and `AllowOperationsMatching` exempt an inline query from persisted-operation
enforcement by the `operationName` in the request body, falling back to the `documentId` or `id`
when there is none. Both are chosen by the caller, and neither says anything about what the
document selects. That stays the behaviour. The allowlist is documented, at the API and on the
docs pages, as a convenience for developer tools and not as a security control.

Because of that, a host that requires persisted operations (`RequirePersisted(true)`) and declares
an allowlist starts only in Development. In any other environment
`PersistedOperationsAllowlistEnvironmentValidator` fails it at startup and names the environment.
With enforcement off the allowlist only keeps names out of the shadow log, and it starts anywhere.

## Status

**Accepted.**

## Why the docs and not the behaviour

The names read as a property of the operation (`AllowOperations("HealthProbe")`), and they are
really a property of the request. That gap is the risk: a host reaching for
`AllowOperationsMatching` to admit a second client's catalogue would be writing an exemption for
anyone who picks a matching name. Closing the gap in the docs costs nothing and breaks no host.

Closing it in the behaviour would not buy a control either. Enforcement is not the authorization
boundary: per-type `[TraxAuthorize]` decides who may run what whether or not a document is
persisted, and the startup reporter says so. The allowlist's job is to let a smoke test or a
developer tool through on a network the host already trusts, and for that a name is the right key.

## Considered options

**Bind each entry to the document it was registered with.** An entry would name a document (its
hash) as well as its name, and only that document would pass. That is a second, unmanaged
persisted-operation store, as the hash option below says, and it makes the host carry document text
in its startup code to admit a probe. The persisted store already does this with history and
invalidation.

**Refuse the allowlist under enforcement outside Development.** Chosen. The docs already showed the
allowlist declared inside an `IsDevelopment()` check, no sample or Trax host declares one
elsewhere, and a health probe that selects only `__typename` passes as introspection without it.
A host that needs a fixed document outside Development persists it.

**Match the name of the operation in the parsed document.** The middleware parses the document
anyway, so this looks cheap. It changes nothing: the caller writes the document, so the caller
names its operations too. It would stop only a request whose `operationName` disagrees with its
document, which HotChocolate refuses on its own.

**Match a hash of the document.** This is a real control, and it is what persisted operations
already are. An allowlist of hashes is a second, unmanaged persisted-operation store, without the
schema validation, shape diffs, history and invalidation the real store has. A host that needs a
fixed document admitted from an untrusted client should persist it.

**Remove the allowlist.** Hosts use it for health probes and dev carve-outs, and the replacement
(persist every probe) is heavier than the risk the docs now name.

## Consequences

**A host that declared an allowlist outside Development under enforcement no longer starts.** The
message names the environment and the two ways out: persist the documents, or declare the
allowlist only in Development.

**A predicate is only as narrow as the callers that can reach the endpoint.** `id =>
id.StartsWith("dev_")` exempts any caller who prefixes a name with `dev_`. The XML docs on both
methods, `PersistedOperationsOptions`, and the persisted-operations pages say this where a host
will read it.

## Exemplars

- `AllowlistKeysOnCallerSuppliedNameTests` pins the behaviour this records: an allowlisted name
  admits a document that selects something else, the same document without the name is refused,
  a predicate is handed the request's `operationName` and never the document, and an unnamed
  request is matched on its caller-supplied id.
- `AllowlistStartsOnlyInDevelopmentTests` pins the startup rule: a name or a predicate under
  enforcement fails outside Development and with no host environment, and starts in Development,
  without an allowlist, and in shadow mode; `UsePersistedOperations` registers the gate.
- [Persisted operations](/docs/persisted-operations#allowlist-and-dev-carve-outs) is where the
  warning is stated for hosts.

Not covered: a host that runs production traffic under the Development environment name gets the
allowlist. The environment name is the host's claim, and the enforcement reporter logs the posture
at startup.

## Changelog

- **2026-10-29**: Under `RequirePersisted(true)` a host with an allowlist starts only in
  Development.
- **2026-09-27**: Recorded.
