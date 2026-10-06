# Trax.Api.Dashboard

A React (Vite) dashboard for Trax.Api. It reads the administrative GraphQL surface exposed
by `Trax.Api.GraphQL` (`operations.*`) and renders the same operational view as the Blazor
`Trax.Dashboard`, but as a standalone single-page app.

It lives in Trax.Website, as its own npm project in `dashboard/` with its own lockfile, configs
and tests, because traxsharp.net serves a demo of it at `/dashboard` (see [The demo](#the-demo)).
Nothing in it imports from the site, so moving it to a repository of its own is a `git mv` and
a CI job.

It covers the full operational surface: Overview (metrics + charts + health, panels chosen in
User settings), Executions (keyset list with failure-class, external id, parent and host filters,
parent and running-junction columns, live feed; detail with parent/child tree, the run's junction
timeline read from `junctionRuns` and kept live by `onJunctionEvent`, its recorded decisions with
withheld answers and tracks, a replay that was abandoned, its log oldest first, cancel/re-queue
including ask-afresh), Work queue (subject and manifest filters, staging, the run an entry replays;
detail with masked input and what an entry waits on), Dead letters (manifest filter;
requeue/acknowledge single, selected or all, with ask-afresh; detail with the manifest, latest
failed run and failed-run history), Manifests and manifest groups (edit, trigger, batch
trigger/ask-afresh and group trigger/cancel in one call with their counts and notes, batch
enable/disable, replay-on-retry, hide admin trains, dependency DAG), Logs (run, level, category and
message filters, either order, "10,000+" for a capped text-filtered count), Trains (Queue and Run
dialogs built from each train's input schema, warning when Run bypasses a subject key), Effects
(toggle, save/discard, a Configure dialog built from each setting's kind that never shows a
sensitive value), Settings (scheduler settings and runtime log levels), and Persisted operations
(list, history, upload with shape-diff guardrail, deactivate/restore), which the sidebar shows
only when the host calls `UsePersistedOperations`. The header shows the host's environment, a
"last refresh failed" mark, the auto-refresh countdown and the UTC time; the footer shows the Trax
version the API runs. It can also run entirely offline against an in-process GraphQL mock (see
[GraphQL mock](#graphql-mock-offline-dev)), or, as the demo build, against recordings of real
hosts (see [The demo](#the-demo)).

## Running

The dashboard talks to whatever host embeds `Trax.Api.GraphQL`. Point the dev proxy at it
and start Vite:

```bash
npm ci
TRAX_API_TARGET=http://localhost:5310 npm run dev   # defaults to http://localhost:5310
```

Open http://localhost:5173, paste an API key when prompted (sent as `X-Api-Key`), and the
app connects. Vite proxies `/trax` (HTTP and the subscription WebSocket) to the API host, so
the browser sees a same-origin API and CORS is not involved in development.

## The demo

`npm run build:demo` (`vite build --mode demo`) builds the dashboard as traxsharp.net serves it:
no API, no credential, and every answer taken from recordings of real Trax hosts. It is the same
app; `src/demo` replaces the urql client, and a banner on every page says the data is recorded
and changes are simulated. The build lands in `dist-demo/`, based at `/dashboard/demo/`
(`DASHBOARD_DEMO_BASE` moves it), and the site's `scripts/build-dashboard-demo.sh` copies it into
`public/dashboard/demo`, which `/dashboard` frames.

**The recordings.** `src/demo/data/dashboard-recordings.json` is what three Trax.Samples hosts
(Scheduling, Recovery and PersistedOperations) answered to every query the dashboard can send,
recorded by Trax.Samples' `scripts/recordings/dashboard.mjs`. That script starts the hosts on
databases of its own, drives them for about three minutes, pauses their schedulers, then walks
every list once per filter, sort and scope, reads every row's page, sends every write, and keeps
every live event with its timing. The hosts are combined as one cluster on one database would
show them, with ids renumbered into one sequence; the script's header has the details. Re-record
from a Trax.Samples checkout beside this repository:

```bash
cd ../Trax.Samples/scripts/recordings && npm ci
TRAX_PG_PORT=5433 node dashboard.mjs --copy-to ../../../Trax.Website/dashboard/src/demo/data
```

then run `npm test` here.

**How it answers** (`src/demo/lookup.ts`, `client.ts`, `replay.ts`):

- A query is answered with the recording for exactly its variables. The only exceptions are
  what a person types, which no recording can enumerate: a train name, an external id, a time
  range, a name or message filter. Those are answered from the recorded list without that filter,
  filtered here the way the API filters it. A query nothing answers gets an error saying so; the
  demo never falls back to invented data, and it does not carry the auto-mock schema.
- A write goes through the mock's overlays (`src/mock/store/overlays.ts`), so the change reads
  back on every page until the tab reloads. Its answer is the host's: a write recorded with the
  same variables against the rows as the recording shows them answers with that recording, a
  refusal included, and otherwise the overlay's answer takes a recorded message that names no row.
- `onTrainStateChanged` and `onDataChanged` replay the frames recorded while the hosts ran, at
  their recorded pace, on a loop, each stamped with the time it plays. Every recorded run had
  finished before the snapshot, so a run's `onJunctionEvent` stays quiet: its timeline shows the
  stored steps.
- Every time in the data is moved forward by whole minutes, so the last recorded moment is about
  when the page loaded.

**The strict check.** `src/demo/demo-pages.test.tsx` drives the demo app as a visitor would:
every page with admin trains hidden and shown, every option of every filter and sort, every page
of every list, every recorded row's page, and every link on every page it reaches. It fails on
any query answered by anything but a recording. `src/demo/demo-stories.test.tsx` runs every
story and its play function on the recordings instead of the mock (`src/demo/story-source.ts`
switches the Storybook harness) and fails on any query a story's page sends that the recordings
cannot answer, other than a scenario's own row id and a filter the play types.

## How it talks to the API

- **Client**: [urql](https://formidable.com/open-source/urql/) over the Vite proxy at
  `/trax/graphql`, with a `graphql-ws` link for subscriptions (`src/lib/graphql.ts`).
- **Auth**: the API key is stored in `localStorage` (`src/lib/auth.ts`) and sent as the
  `X-Api-Key` header on every request. Browsers cannot set headers on a WebSocket upgrade,
  so subscriptions send the key in the `graphql-ws` `connection_init` payload, which the
  API's `TraxApiKeySocketInterceptor` reads.

## GraphQL mock (offline dev)

The dashboard can run with no backend, no API key, and no Postgres, resolving GraphQL
in-process. Use it for component work, demos, Storybook, and CI.

```bash
npm run dev:mock     # whole dev server mocked (no devhost, no key)
```

Or flip a single tab of a normal `npm run dev` to mock by appending `?mock` to the URL
(`http://localhost:5173/?mock`). In mock mode the sidebar shows a **MOCK** badge and a
**🧪 Mock overlay** link (`/dev/mock-overlay`) that renders the session's writes and events.

### How it works

Three layers compose as urql exchanges (`src/mock/`), fixtures-first with an auto-mock
fallback:

1. **Auto-mock schema** (`build-mock-schema.ts`) over `@urql/exchange-execute`. Every field
   resolves to plausible fake data from the exported SDL, so nothing errors.
2. **Fixtures** (`fixtures/index.ts`, replayed by `fixture-exchange.ts`) — a committed snapshot
   of real (anonymized) responses, keyed by a hash of the variables, so pages render realistic
   data. Detail requests fall through to auto-mock rather than serve the wrong record.
3. **Stateful overlays** (`store/overlays.ts`) — mutations write an in-memory delta and reads
   merge it back, so a change sticks (read-after-write). Subscriptions are driven by a
   synthetic event `simulator.ts`, so the live feed animates offline.

The mock lives behind a dynamic import gated on `import.meta.env.DEV`, so a production build
excludes it entirely. `npm run build` checks that (`scripts/check-bundle.mjs`): the bundle must
carry no embedded schema, no mock store, no recordings and no demo banner.

### Commands

| Command | What it does |
|---|---|
| `npm run dev:mock` | dev server against the mock |
| `npm run build:demo` | the demo build, on the recordings (see [The demo](#the-demo)) |
| `npm run mock:schema` | re-export the SDL from a running devhost (introspection) |
| `npm run mock:capture` | re-capture fixtures from a running devhost |
| `npm run test` | vitest: overlay read-after-write + every story renders |
| `npm run storybook` | stories against the mock; a story's `parameters.real` runs it against the devhost |

### Extending it

- **New realistic data**: add the operation to `scripts/capture-fixtures.ts` and run
  `npm run mock:capture` (needs the devhost). `FIXTURE_MAX_PAGES` caps the page walk.
- **A new write with effect**: add a `StatefulOverlay` in `store/overlays.ts` — a `mutations`
  entry that writes the store and returns the ACK, and a `queries` entry that merges the delta
  over the read. Register it in `defaultOverlays`. The `wrap` / `mapItemsAtPath` /
  `patchObjectAtPath` helpers keep each overlay to a few lines.
- **A host that answers differently**: a story layers its own overlay over the defaults with
  `parameters.overlays` (see `noLogLevelServiceOverlay`, a host without `AddScheduler`).

## Testing

Two tiers, deliberately separate:

- **`npm run test`** — fast, offline. Runs the demo's strict check (see [The demo](#the-demo)) and,
  against the mock (fixtures + auto-mock + overlays):
  overlay read-after-write, plus Storybook `play` functions exercising **every control** and its
  edge cases. Per page: filters (individually and in combination), sort, keyset pagination
  (including the first/last-page button boundaries and filter-resets-to-page-1), bulk selection
  (partial vs full "select all", only-actionable rows selectable, selection clearing on filter),
  the cancel/requeue/acknowledge/edit flows including the note modal, the live feed, and each
  empty/error/empty-from-filter state. Per component: form validation (required/blank, digit and
  decimal sanitisation, submit gating), Modal/dialog Escape + backdrop close, Pager callbacks, and
  state-dependent rendering (a cancelled execution hides its Cancel button, each TrainState renders
  its label). No backend, deterministic, CI-safe.
- **`npm run test:e2e`** — write-path end-to-end against a **real devhost and Postgres**. It
  spins up a disposable database (`trax_dashboard_e2e`) and a dedicated devhost on :5311, seeds a
  small known dataset ([scripts/e2e-seed.sql](scripts/e2e-seed.sql): runs with recorded junction
steps and recorded decisions (one withheld, as a `[TraxSensitive]` question's is), a run's log and
enough heartbeat entries to cap a text filter's count, a staged subject-keyed work queue entry,
manifests and a group kept for the batch triggers, persisted operations), then runs
  [src/e2e](src/e2e): a schema-drift check (live schema vs the committed SDL), read contracts
  (every query returns the shape the UI expects), and the real mutation flows (cancel / requeue /
  acknowledge / update, the batch and ask-afresh mutations, queue/run, persisted-operation
  upload/deactivate/restore) each verified by re-querying the server, plus one live
  `onJunctionEvent` subscription against a real run (the devhost's `/dev/delay`). The devhost
  enables `AddJunctionEvents()` and `UsePersistedOperations` (shadow mode) for this, and registers
  the scheduler as an API host does (`AddScheduler`, for the runtime log levels) with its
  background pollers removed, so nothing changes the seeded rows but the tests. Tears the devhost
  down on exit.
  Needs the `trax_stress_db` docker container running.

The e2e mutates its own throwaway DB, so it never touches the stress/demo data. It's not part of
the default CI job (it needs the .NET backend); run it locally or as a separate job.

## Design constraints (from stress testing)

The admin endpoints are stress-tested to millions of rows in `Trax.Api.Tests.Stress`. Two
findings shape this UI:

- **Keyset pagination only.** Lists page with `nextCursor` → `afterId`, never a deep
  offset `skip` (which scans every skipped row: ~430ms at a 3M-row offset versus ~35ms for
  a keyset page). See `ExecutionsPage`.
- **Poll metrics on an interval.** `operations.metrics.dashboard` runs several aggregations
  over the 7-day window and is the heaviest read (~0.5s at 3M rows). Overview refreshes it
  every 5s rather than on every interaction.

## Layout

```
src/
  lib/graphql.ts        urql client (HTTP + graphql-ws) with X-Api-Key auth
  lib/auth.ts           API key storage
  graphql/              operations.* queries, mutations, subscriptions
  types.ts              TS mirrors of the Trax.Api DTOs / enums
  components/           Layout, ConnectionGate, StateBadge, charts, dialogs, toasts
  pages/                one component per route (+ *.stories.tsx)
  demo/                 the demo build: recordings, the client that answers from them, the replay
    data/                 dashboard-recordings.json (generated by Trax.Samples)
  mock/                 offline GraphQL mock (see "GraphQL mock" above)
    client.ts             createMockClient / createRealClient
    build-mock-schema.ts  auto-mock schema over the exported SDL
    fixture-exchange.ts   replays captured fixtures
    fixtures/index.ts     captured snapshot (generated)
    store/                mock-store, stateful-exchange, overlays
    simulator.ts          synthetic subscription events
scripts/                export-schema, capture-fixtures, smokes, check-bundle
```
