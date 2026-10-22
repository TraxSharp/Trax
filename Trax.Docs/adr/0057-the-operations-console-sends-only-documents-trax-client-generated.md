---
authors: [Theauxm]
repos: [api, cli]
areas: [graphql]
status: accepted
---

# The operations console sends only documents trax client generated

The operations console (`Trax.Api.Dashboard`) sends every request through a client `trax client --operations`
generates from its documents, one operation per `.graphql` file in `ClientApp/src/graphql`, and from the operations
schema in `ClientApp/operations.graphql`. The generated runtime owns the transport and the persisted ids; the console
adds only what a page needs on top (a cache, hooks, the in-flight count). The point is that a second operator
frontend, such as a terminal UI on a C# client, sends the same documents through the same kind of client, rather
than copying a React app's GraphQL layer.

## Status

**Accepted.**

## Decisions

- **The operations surface stays out of a host's client manifest.** `--operations` reads the schema's SDL and the
  console's documents, never a manifest, and a host still refuses at start a `trax_client_` document that reaches
  `operations` (ADR 0053). The console is the same on every host, so its client is generated once, in this
  repository, and committed.
- **Registration does not change.** A console document is still stored under `trax_console_<name>_<hash>`, checked by
  the host's own rules and registered from the embedded `console-documents.json` ([Trax.Api ADR 0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md));
  the generator now writes that file. Registering a document changes which shapes may run, never who may run them:
  the operations gate decides that on every request.
- **The generator holds a document to the host's rules for a console document** (one named operation, no directive,
  no alias at the root, a query or mutation under `operations` alone or `persistedOperations` alone, a subscription
  to one operator stream), and to the schema, as it holds a client document to the manifest. Persisted-operation
  management gets no id: a host runs it as text behind its own gate.
- **The ids are the host's.** `ClientDocumentSet.ConsoleIdOf` and `ConsoleDocuments.IdOf` give a text the same id,
  held equal by a fixture both suites read.

## Considered options

- **List the operations surface in every host's client manifest, and let any frontend's documents reach it.**
  Rejected: it removes the start-time refusal of a client document that reaches `operations`, so a public frontend
  could register persisted documents shaped against the admin surface, and every frontend repository would carry the
  admin schema. Nothing would become callable without the operations gate, but a fail-closed check would be gone
  for no gain: the console's surface does not vary by host.
- **A hand-written operations client per language.** Rejected: two sources for one set of documents, and no check
  that a document is one the host registers.
- **Keep urql and point its fetch at the generated ids.** Rejected: the console's requests would still not be a Trax
  client, which is what a second frontend reuses.

## Consequences

- A changed document needs `npm run client` in `ClientApp` (it runs `trax client` with the .NET SDK) and the result
  committed; CI's `api-dashboard` job fails on a stale client, and runs when Trax.Cli changes too.
- The ids changed once, since a document's text no longer carries the `__typename` urql added to every selection.
  The embedded file and the app are built together, so a host serves the ids its console sends.

## Exemplars

**Enforced elsewhere:** `OperationsClientGeneratorTests` in `Trax.Cli.Tests` (the rules, the types, the ids, the file,
and that `--operations` reads no manifest), `ConsoleDocumentsTests.The_host_and_trax_client_name_a_console_document_alike`
in `Trax.Api.Tests`, both reading `Trax.Api/tests/Trax.Api.Tests/PersistedOperations/Fixtures/console-document-ids.json`,
and the `api-dashboard` job's "The committed client matches the documents" step, which runs `npm run client:check`.

- [trax client](/docs/reference/cli#the-operations-consoles-client) is the rule as published.

Not covered: nothing stops a page from calling `fetch` itself; review catches that.

## Changelog

- **2026-10-22**: Recorded.
