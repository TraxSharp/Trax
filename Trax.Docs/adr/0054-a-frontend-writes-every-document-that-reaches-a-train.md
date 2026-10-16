---
authors: [Theauxm]
repos: [api, cli, samples]
areas: [graphql]
status: accepted
---

# A frontend writes every document that reaches a train

A host no longer generates a persisted document for each exposed train. A frontend reaches a train only through a
document it wrote: a client document (`trax client --documents`, registered by the host at boot, [0053](./0053-a-client-document-is-registered-by-the-host-at-boot.md))
or, from .NET, a typed request whose result type declares the selection. The client manifest lists each train's
field, arguments, answer and requirements, and every type its arguments and answer reach, so a document can select
any of it.

## Status

**Accepted.** Replaces [Trax.Api ADR 0040](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/docs/adr/0040-every-exposed-train-is-a-persisted-document-the-host-registers.md),
which is deprecated.

## Why this is written down

A generated document selected everything the answer reached, cutting only a field that led back to a type already on
the path. Its size is then the number of paths through the answer, not the number of types or the depth: seven types
that all reference each other hold 63 fields and generated 7,828; three wide and seven deep with no back-reference at
all generated 4,372; twenty entities with about three navigations each generated 1,419,040, and forty did not finish
in twenty seconds. Past HotChocolate's parser limit of 2048 fields the host could not start, with no train named.
Under it, the host started and then refused every call for cost (`MaxFieldCost` is checked per request, not at boot),
so five such entities, 260 fields, already made the train uncallable.

No cut fixes this without choosing a selection for the client. Any finite cut is a policy (stop at the first entity,
a depth, a cost budget, one named fragment per type), and which one is right depends on the screen, which only the
client knows. A GraphQL client lets the caller declare the selection, and Trax's own .NET client already does
(`Trax.Api.GraphQL.Client.Typed` builds a query from the caller's result type and refuses one that refers back to
itself). The host-side generator was the exception.

## Decisions

- **The host generates no train document.** It registers machine documents (ADR 0048), the `runProgress` pair, whose
  shapes are fixed and walk no consumer type, and the client documents a frontend wrote.
- **The `trax_train_` prefix stays reserved.** No upload may use it. A row an earlier version stored under it is
  collected by the grace-period sweep, like any version no node serves, and a request for one is answered
  `SERVER_OUTDATED` while a node of that version may still serve it and `CLIENT_OUTDATED` once it is collected, so a page
  built against a generated train function reloads.
- **The manifest is format 3.** A train lists its arguments where it listed a document; `Types` holds the full reach of
  every exposed train's arguments and answer. An older `trax client` refuses the file rather than generating
  functions with no document.
- **`trax client` writes no `trains/` module.** A train's typed function comes from a client document. A `trains/`
  folder an earlier version wrote is removed as stale.

## Considered options

- **Stop at the first entity**: non-entity types in full, an entity's navigations one hop deep with scalars only.
  Rejected: a fixed cut still chooses the selection for the client, and the deeper reads would have needed a client
  document anyway.
- **A breadth-first walk bounded by the host's cost and depth.** Rejected: the shape a function answers would move
  when an unrelated navigation is added or a host limit changes, moving the id and the TypeScript type with no change
  to the train.
- **One named fragment per type.** Prototyped: the document shrank from 7,828 fields to 58, but HotChocolate expands
  fragments before it costs and runs an operation, so the expanded selection was still refused for cost, and a
  depth-first plan made chains past `MaxExecutionDepth`.
- **A `trax client --scaffold` that writes a starter `.graphql` per train.** Not taken now; it is a convenience that
  can be added without changing this decision.

## Exemplars

**Enforced elsewhere:** `GeneratedTrainDocumentsRemovedE2ETests` in `Trax.Api.Tests.StateMachine.E2E` (a host exposing
a train answering seven types that each list the others starts and stores no `trax_train_` row, a client document
reaches that train and follows a back-reference the frontend chose, an earlier version's train id is answered as
outdated with a 4xx, and the manifest lists each train's arguments and every type its answer reaches),
`TrainDocumentsTests` in `Trax.Api.Tests` (an earlier train id still reads back to a slot and the prefix stays
reserved), `ClientGeneratorTests` in `Trax.Cli.Tests` (no `trains/` module is written; a train's function comes from a
client document), and CI's sample client checks, which run `trax client --check --documents` over the Recovery and
StateMachine pages.

- [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) is the rule as published.

Not covered: the first rolling deploy, while nodes of an earlier version still serve generated train documents, which
no single-version test can run.

## Changelog

- **2026-10-16**: Recorded.
