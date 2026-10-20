---
name: trax-troubleshoot
description: Use when a Trax host will not start, throws at registration or startup, or a Trax run, GraphQL call, scheduled job or state-machine event fails and the cause is unclear. Covers pasted Trax errors such as "registered trains cannot run", "cannot be built", "Could not find train with input type", "Call AddEffects(...) before AddMediator(...)", "AddScheduler() requires a data provider", "exposure authorization check failed", TraxPrincipalNotAvailableException, TRAX_* and PERSISTED_OPERATION_* error codes, Unexpected Execution Error, runs stuck or Failed, jobs that never execute, and database or provider problems with Postgres, SQLite or InMemory. Maps the message to the docs page that fixes it, shows how to read a run's record in trax.metadata, trax.log and the operations console, and lists what to collect before asking the user.
metadata:
  internal: true
---

# Troubleshoot a Trax app

This skill routes. It holds no fixes of its own: every fix is on the page a message points to, and
that page is the one to follow. Find the page, read the entry, then change code.

## First: read the whole message

- **Trax refuses loudly and names the cause.** A host with a chain that can never run, a
  registration in the wrong order, a missing provider or a missing security posture stops before
  it serves a request, and the message says what to change. Read every line of it: a chain
  refusal lists every broken train, not only the first.
- **Many messages end with a link**, `See https://traxsharp.net/docs/cross-cutting/troubleshooting#...`.
  Open it. Without a link, search
  [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting) for a distinctive
  phrase: each entry is headed by the message as the source writes it.
- **Do not make the error go away by switching a check off.** Skipping the startup chain check,
  removing a posture, turning off persisted operations or Origin checks, or moving a credential out
  of the Development branch hides the fault and opens the host. If the right fix really is to relax
  something, ask the user and say what it costs.

## Map the message to its page

| The message begins, or the symptom is | Read |
|---|---|
| `<n> of <m> registered trains cannot run:` | [Startup chain check](https://traxsharp.net/docs/core/startup-chain-check#refusals), then the troubleshooting entry for each line's kind |
| `... cannot be built: its constructor needs '...', which is not registered` | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting#x-cannot-be-built-its-constructor-needs-y-which-is-not-registered) |
| `Junction '...' (train '...') needs '...' as its input, but nothing earlier in the chain produced one` | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting#junction-x-train-y-needs-z-as-its-input-but-nothing-earlier-in-the-chain-produced-one) |
| `Could not find train with input type (...)` | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting#could-not-find-train-with-input-type-x) |
| `Call AddEffects(...) before AddMediator(...).`, `AddTraxGraphQL() requires AddTrax() to be called first`, a train registered after `AddTraxGraphQL()` | [Registration order](https://traxsharp.net/docs/reference/registration-order) |
| `AddScheduler() requires a data provider`, a scheduler value out of range, a manifest group or batch conflict, a runner with no posture | [Scheduler startup errors](https://traxsharp.net/docs/scheduler/startup-errors) and [Value ranges](https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#value-ranges) |
| `Invalid cron expression`, `A schedule interval must be at least one second.` | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting) |
| `Trax GraphQL exposure authorization check failed:`, a policy `not registered`, an `operations` namespace with no gate, a `do-not-use-in-production` key outside Development | [Startup refusals](https://traxsharp.net/docs/security/startup-refusals) |
| `UseTraxApiDashboard() ...` | [Add the console to a host](https://traxsharp.net/docs/dashboard/add-the-console-to-a-host#troubleshooting) |
| `Unable to resolve service for type 'Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider'` | [Registration order](https://traxsharp.net/docs/reference/registration-order#missing-registrations-and-what-they-raise) |
| `TraxPrincipalNotAvailableException` | [Inject the current principal](https://traxsharp.net/docs/security/inject-the-current-principal) |
| A GraphQL error with an `extensions.code` (`TRAX_AUTHORIZATION`, `TRAX_TRAIN_ERROR`, `TRAX_INVALID_INPUT`, `PERSISTED_OPERATION_REQUIRED`, `CLIENT_OUTDATED`, ...) | [Error codes](https://traxsharp.net/docs/sdk-reference/graphql-api/error-codes) |
| `Unexpected Execution Error`, or `The train failed.` | The real failure is masked from the caller on purpose. Read the run's record (below) and the server log. [Error codes](https://traxsharp.net/docs/sdk-reference/graphql-api/error-codes) says which exceptions pass through |
| A machine answer with a `problem` (`GUARD_FAILED`, `NO_TRANSITION`, `FORBIDDEN`, ...) | [Result codes](https://traxsharp.net/docs/sdk-reference/statemachine-api/result-codes): a refusal is data, nothing was written |
| A browser WebSocket upgrade answered `403`, while a Node client works | [Allowed origins](https://traxsharp.net/docs/sdk-reference/graphql-api/subscriptions#allowed-origins) |
| A run shows `Failed` though the train looked fine | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting#train-completes-but-metadata-shows-failed) |
| Scheduled jobs never run, with no error | [Troubleshooting](https://traxsharp.net/docs/cross-cutting/troubleshooting#scheduled-jobs-dont-execute-no-errors) |

An error from a skill's own area (a train, a machine, the API) usually has a known trap in that
skill too: `trax-write-a-train`, `trax-design-a-machine`, `trax-expose-over-api`,
`trax-schedule-work`.

## Provider and database problems

Check which provider the host uses (`UseInMemory`, `UseSqlite`, `UsePostgres` in `AddEffects`)
before anything else.

- **InMemory** keeps runs in the process and loses them on restart. Nothing dispatches the work
  queue, so a queued run is refused, and a machine that invokes trains needs Postgres or SQLite.
- **SQLite** serializes writes at the file and fits one process on one machine. Two Trax
  processes on one file, such as a hub and a worker, need Postgres.
  [SQLite and Postgres](https://traxsharp.net/docs/effect/sqlite-and-postgres) has the table.
- **Postgres**: the Trax tables live in the `trax` schema, and applied migrations are listed in
  `trax.migrations`. A schema older than the packages is a migration that did not run; see
  [Schema migrations](https://traxsharp.net/docs/reference/schema-migrations) and
  [Run migrations outside startup](https://traxsharp.net/docs/effect/run-migrations-outside-startup).
- Moving a template off InMemory is [Switch a template to Postgres](https://traxsharp.net/docs/reference/switch-a-template-to-postgres).

## Read a run's record

Every run of a `ServiceTrain` writes a row to `trax.metadata`; it is the whole record of the run.
The tables are listed on [Trax data model](https://traxsharp.net/docs/effect/data-model#tables).

1. **Find the run.** Its `external_id` is what a mutation answers and what crosses process
   boundaries. `name` is the train's interface full name (`MyApp.Trains.IOrderTrain`), not the class.
2. **Read its failure fields**: `train_state`, `failure_junction`, `failure_exception`,
   `failure_reason`, `stack_trace`. What each holds is on
   [Metadata](https://traxsharp.net/docs/effect/metadata#failure-fields).
3. **Read its log lines.** With `AddDataContextLogging()`, every host on the database writes to
   `trax.log`, and each line written during a run names it in `metadata_id`. There is no timestamp
   column, levels are lowercase, and `category` is the class name, not the interface. The queries
   and traps are on
   [Debug across processes with the log table](https://traxsharp.net/docs/effect/debug-across-processes-with-the-log-table).

   ```sql
   select external_id, name, train_state, failure_junction, failure_exception, failure_reason
   from trax.metadata where external_id = '<external-id>';

   select l.id, l.level, l.category, l.message, l.exception
   from trax.log l join trax.metadata m on m.id = l.metadata_id
   where m.external_id = '<external-id>' order by l.id;
   ```

4. **Steps and decisions**, if the host records them: `trax.junction_run` (with `AddJunctionEvents()`)
   says which step failed and how; `trax.decision` (with `AddDecisionRecording()`) what each
   decision answered.
5. **The operations console**, if the host serves it, shows the same record:
   [A run](https://traxsharp.net/docs/dashboard/a-run) draws it on its chain with the failing step,
   [Runs](https://traxsharp.net/docs/dashboard/runs) lists failed runs, queued entries and dead
   letters, and [System](https://traxsharp.net/docs/dashboard/system#logs) reads the log table.

A missing log line is not proof nothing was logged: the log table drops entries under bursts.
Check the console or the host's own log output too.

## Collect this before asking the user

When the pages do not settle it, ask once, with everything the user would otherwise ask for:

- the full message, every line, exactly as printed, and whether it came at registration, at host
  start or on a request;
- the Trax package version (every `Trax.*` package should be at one version) and the data provider;
- the environment the host ran in (Development or not), since demo credentials exist only there;
- the registration section of `Program.cs`, in order;
- for a failed run: its `external_id`, its `trax.metadata` failure fields and its `trax.log` lines;
- for a GraphQL failure: the `extensions.code`, the operation, and whether the caller was a
  browser, a Node client or the IDE;
- which docs entry you followed and what happened when you did.
