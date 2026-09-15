---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# The operations namespace gates independently of the endpoint

Exposing the `operations` namespace forces a choice, and until now there were only two:
`RequireAuthorization()`, which gates the whole endpoint, or `AllowAnonymousOperations()`, which
publishes the scheduler control plane. A host with public pre-login surfaces cannot take the
first, so it takes the second, and gets a public control plane it did not want.
`GateOperations(policy, roles)` is the third answer: `@authorize` on the `operations` field of
both root types, and the rest of the endpoint untouched.

The check that forces the choice now covers the read surface too. It tested
`OperationMutationsExposed` alone, so `operations { hosts }`, `logs`, `executions`, `config` and
the rest were reachable with no acknowledgement required at all. Those report internal
hostnames, private-network topology and workload volume, which is a disclosure whether or not
the mutations beside them are gated.

## Status

**Accepted.**

## Considered options

**Leaving the read surface out.** What the code did, and the reasoning was that queries are
read-only so the guard is about the mutations. The audit that produced this ADR read three
internal hostnames and their execution counts out of a deployed environment with no credentials.
Read-only is not the same as harmless.

**Gating the namespace by default.** Safer on its face, and it would break every host that
exposes operations today, including the ones whose endpoint gate already covers it. The
acknowledgement is the cheaper instrument: it costs one call, it appears in the diff, and it
cannot be arrived at by forgetting.

**An `operations`-specific policy name that Trax defines.** Rejected because it invents a second
authorization vocabulary next to the one `[TraxAuthorize]` already uses. `GateOperations` takes
the same policy and roles, combined by the same code, so a host that gates a train and the
namespace the same way gets the same rules.

## Consequences

**This is a behaviour change for hosts that exposed only queries under an open endpoint.** They
now fail at startup until they answer. That is the point, and the message names all three
answers rather than only the two it used to offer.

**`AllowAnonymousOperations()` means what its name says.** It was the only escape from a startup
exception, so calling it recorded nothing about intent. With a third answer available, reaching
for it is a decision again.

**Persisted operations no longer forces the namespace.** `UsePersistedOperations` grafted it on
unconditionally, so taking enforcement meant taking a GraphQL-exposed scheduler console.
`ExposeOperationsNamespace(false)` keeps storage, enforcement, cache and invalidation, and
leaves the namespace out of the schema.

## Exemplars

- `OperationsExposureTests` pins the three answers, the two contradictions
  (`AllowAnonymousOperations` with either gate), the dead-configuration case, and that the
  directive reaches both root fields and is absent without the gate.
- `AdminOperationsAuthorizationTests` drives all of it over HTTP, including the case the feature
  exists for: with the namespace gated, an anonymous caller is refused there and still served by
  a public field on the same endpoint.
- `ExtensionMethodTests` covers declining the namespace from `UsePersistedOperations` while
  enforcement and storage stay registered.

Not covered: nothing checks that the policy or roles passed to `GateOperations` are registered
with ASP.NET Core. An unknown policy name fails at request time, the same way it does for
`[TraxAuthorize]`.

## Changelog

- **2026-09-15**: Recorded.
