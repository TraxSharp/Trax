---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# Operations holds only what the dashboard does

The `operations` namespace is the admin surface: every field under it is something an operator does or reads at the
operations dashboard, through the same service call the dashboard makes (central `docs/0022`). Anything a deploy
pipeline, another service or a monitor needs lives outside it, under a namespace and a gate of its own, even when the
dashboard also uses it.

## Context

`operations` had one gate, `GateOperations(...)`, and it was the obvious place for any field that needed a
credential. Persisted-operation uploads, the machine-document manifest and the health read all landed there. A host
then could not give a deploy pipeline or a monitor a credential narrower than the operator's: the uploader could also
cancel runs, requeue dead letters and change effects. Turning on persisted operations even exposed the namespace on its
own (`UsePersistedOperations` called `ExposeOperationQueries()` and `ExposeOperationMutations()`).

## Decision

- Every field under `operations`, at any depth of its nested namespaces, is selected by a GraphQL document of the
  operations dashboard (Trax.Api.Dashboard's React app). The guard reads those documents.
- A field with a caller other than an operator at the dashboard goes elsewhere, with its own gate. The first is
  persisted-operation management: `persistedOperations { ... }` at the root, behind `GateManagement(...)`, and not
  served at all until a host declares that gate.
- A field the dashboard and another caller both need belongs to the other caller's surface, and the dashboard calls it
  there; the dashboard's operator then has to meet that surface's gate too. Dashboard/API parity still holds in the
  other direction: every dashboard action is reachable in GraphQL through the same service call.
- Triggering a manifest or group, queueing a train and requeueing a run or a dead letter stay under `operations`:
  in 2026-10 only people at the dashboard do them. The first field a service needs for that kind of work goes to a
  root `automation { ... }` namespace behind its own gate (`GateAutomation(...)`), served through the same
  `IOperationsService` calls, and the dashboard calls it there. Resume stays an operator action: it is chosen after
  someone has read the failure.
- `health` and `metrics` stay under `operations` as the dashboard's home page reads them. A scraper reads
  `/trax/health`, which `AddTraxHealthCheck` serves outside GraphQL.
- The direction this adds to the parity rule is checked. Its first half (every dashboard action exists in GraphQL) is
  `docs/0022`'s.

## Considered options

- **One schema per audience.** Rejected: each is another executor, document set and auth path, and none of the moved
  fields needs types of its own. The split is by namespace and gate inside one schema.
- **Document the rule and leave the namespace as it is.** Rejected: the namespace grows by default, because that is
  where a gate already is.
- **An attribute on each field naming its dashboard page.** Rejected: it can claim a page that does not exist. The
  dashboard's own documents are what it does, so the guard reads them.

## Consequences

- A new field under `operations` that no dashboard document selects fails the build, and has to choose its surface.

## Status

**Accepted.**

## Exemplars

- `OperationsIsAdminOnlyTests` reads the dashboard's documents and fails on any `operations` field none of them
  selects, and on an exception that no longer excuses anything.
- [ADR 0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md) holds the first move: its
  guards show persisted-operation management served only behind its own gate.

Not covered: the Blazor dashboard calls the services directly and is not read; a field the React dashboard selects
only to look busy would pass.

## Changelog

- **2026-10-15**: Recorded.
