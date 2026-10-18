---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# The operations console is registered as persisted documents

The operations console (Trax.Api.Dashboard's React app) sent document text, so a host that required persisted
operations refused every request it made, and a host had to choose between the console and a fail-closed API. A host
that serves the console now registers the console's own documents when it starts, as it registers a machine's and a
frontend's (central `docs/0053`), and tells the page their ids, which the page sends instead of text.

## Decision

- The console's documents are a file the app's build writes from its source (`ClientApp/console-documents.json`,
  checked by its tests) and the package embeds. `UseTraxApiDashboard` hands them to the GraphQL host.
- Each is stored under `trax_console_<name>_<hash>`, content-addressed as a client document is, so any version runs on
  any node while the store holds it active, and a collected one answers `CLIENT_OUTDATED`.
- A console document reaches only `operations` (a query or a mutation) or one of the operator's streams (a
  subscription). Persisted-operation management is left out and still runs as text, behind `GateManagement`, as
  before.
- A document this host cannot serve (the console reaches optional parts of the schema: operations mutations, junction
  events, machines) is left out and logged, not a reason to refuse the start; the console then sends it as text, which
  enforcement refuses, so that page degrades on that host only.
- Registration changes which shapes may run, never who may run them. The operations gate decides every request.
- The ids reach the page in the runtime config the dashboard writes into its entry page. A host that maps the console
  after its hosted services started (a Startup's `Configure`) registers the documents once it has started.

## Considered options

- **Widen `trax_client_` to reach `operations`.** Rejected: `docs/0053` keeps client documents to trains and query
  models so requiring persisted operations still bounds what a frontend runs. The console is a different client with a
  different gate, so it gets its own prefix and its own reach.
- **Allowlist the console's operation names** (`AllowOperations`). Rejected: the allowlist keys on the name the caller
  sends (`docs/adr/0005`), so it would admit any document named like a console operation.
- **Exempt requests to `operations` from enforcement.** Rejected: any text under `operations` would run, which is the
  shape enforcement exists to fix.
- **Have the page compute the ids itself.** Rejected: the host already knows them, and handing them over keeps one hash
  implementation and lets a host leave a document out.

## Status

**Accepted.**

## Exemplars

- `ConsoleDocumentsTests` pins the id scheme, the reach and leaving out a document the host cannot serve.
- `ConsoleDocumentsE2ETests` runs a console document by its id with persisted operations required, refuses its text,
  keeps the operations gate, and checks the page is told the ids only on a persisted host.

Not covered: no browser test loads every console page against a host that requires persisted operations; the end-to-end
tests call documents directly.

## Changelog

- **2026-10-18**: Recorded.
