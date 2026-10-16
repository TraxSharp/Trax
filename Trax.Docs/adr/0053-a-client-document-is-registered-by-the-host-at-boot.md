---
authors: [Theauxm]
repos: [api, cli]
areas: [graphql]
status: accepted
---

# A client document is registered by the host at boot

A frontend fetches what a component needs from a `.graphql` file it writes. (A host once also generated a
select-everything document for each exposed train; it no longer does, see [0054](./0054-a-frontend-writes-every-document-that-reaches-a-train.md).) `trax client --documents <glob>` checks each file against the client
manifest, types its result from what it selects, and writes `trax.documents.json`; the host names that file with
`ClientDocuments(path)` and registers every document in it when it starts, under `trax_client_<name>_<hash of the
text>`, which the generated function sends. A host with persisted operations required runs nothing else a client
writes.

## Status

**Accepted.**

## Decisions

- **The host registers them, at boot.** The file is part of the host's build, so the host checks every document
  against its own schema before it serves anything, and a schema change that breaks a frontend's document refuses
  the start, naming it. The host deploys before the frontend.
- **Trains and query models.** One named `query` or `mutation`, its root `dispatch` or `discover`, under it only the
  host's exposed trains, its query models (in a query) and the namespaces holding them, a mutation exactly one train,
  no directive. Each field is resolved against what the host registered, not what the schema holds, so a hand-written
  field under `discover` is refused. A query model is where a subset matters most: it is projected, so a document that
  selects less reads less. Its `@authorize`, its navigation checks and its row filter still apply per request.
- **A filter or sort is written in the document.** A variable whose type is, or holds, a filter or sort input is
  refused; only its values are variables (`state: { eq: $state }`, `order: { title: $direction }`). Otherwise one
  persisted id would run any filter the model offers, deep `some` / `all` across navigations included, and requiring
  persisted operations would no longer bound which queries run. The operations surface, persisted-operation
  management, machines, `runProgress` and introspection keep their own documents and gates; a client document widens
  which shapes of a train run, never who may run them, since each train's own authorization still applies. The host
  checks the rule itself and trusts nothing the CLI did.
- **Content-addressed, and kept for the grace period.** The id carries the SHA-256 of the text exactly as the file
  holds it, and its check digits are a generated document's, so a stored row is a client document only when its body
  is the one its id names. A version no node registers any more keeps running on every node, while its stored text
  passes that node's checks, until no node has registered it for the grace period of the generated documents; then it
  answers `CLIENT_OUTDATED`. A page built before a frontend's deploy keeps working through it.
- **Answers are typed per document.** Every output type the generated client declares is what some document selects:
  a client document or a named fragment. Nothing declares a model's full shape, so no
  type promises a field the response lacks.

## Considered options

- **TypeScript selection objects evaluated by the CLI.** Rejected: a language only Trax reads, which the CLI would
  have to execute under node, and which could hold only literals.
- **The frontend's pipeline uploads its documents through the management surface.** Rejected: the documents would be
  checked only at upload, so a later host change could break them with nobody seeing it at boot, and the pipeline would
  need the management credential.
- **Numbered per slot, like a train's generated documents were.** Rejected: an older id would answer `CLIENT_OUTDATED`
  as soon as the host deploys, and the pages that reload would get the old frontend until it deploys too.
- **Any operation the schema allows.** Rejected: the widest allow-list, for no use a frontend has.
- **Trains only, with query models left on the management upload.** Rejected (amended 2026-10-16): the reads a
  frontend most often narrows would have had no path to production but the upload this record rejects.
- **A filter or sort input as a variable.** Rejected: cost and authorization would bound it, but the persisted id
  would no longer fix the query. A per-host opt-in was rejected for the same reason.

## Exemplars

**Enforced elsewhere:** `ClientDocumentsTests` in `Trax.Api.Tests` (the file's refusals, what a document may hold,
the id), `ClientDocumentsE2ETests` in `Trax.Api.Tests.StateMachine.E2E` (a document runs by id with persisted
operations required, refusals stop the start, a version another node registered runs, authorization still applies),
`MachineDocumentRegistryTests.A_stored_client_document_of_any_version_is_run_once_it_passes_and_a_collected_one_is_outdated`,
and `ClientDocumentsGeneratorTests` in `Trax.Cli.Tests` (the CLI's rules, the types and the file). Both suites read
`Trax.Api/tests/Trax.Api.Tests/PersistedOperations/Fixtures/client-document-ids.json`, so the CLI and the host give a
text the same id.

- [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) is the rule as published.

Not covered: a stored version whose text a newer node's schema no longer validates is refused per node and cached for
the node's life; no test changes a node's schema while it runs.

## Changelog

- **2026-10-16**: Query models join client documents; filters and sorts are written in the document; each field is
  resolved against what the host registered.
- **2026-10-16**: A client document is now the only way a frontend reaches a train (0054).
- **2026-10-16**: Recorded.
