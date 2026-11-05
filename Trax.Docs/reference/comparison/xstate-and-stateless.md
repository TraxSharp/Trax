---
title: Trax compared with XState and Stateless
description: "How Trax state machines compare with XState plus your own backend, and with Stateless, and when to choose each."
type: explanation
area: general
nav_order: 3
---

# Trax compared with XState and Stateless

Checked on 2026-11-05 against XState 5.33 (6.0 was in prerelease that day) and the Stateless README. Corrections are welcome
as issues.

[XState](https://stately.ai/docs/xstate) is an MIT-licensed TypeScript library for statecharts and actors. It has the
fullest statechart model of the three, and because it is JavaScript the same machine runs unchanged in a browser and
in Node. [Stateless](https://github.com/dotnet-state-machine/stateless) is the usual .NET choice: a small,
Apache-2.0 library for a state machine inside one process. Neither stores an instance, checks who sent an event, or
serves a machine to a client; that is left to the application.

A Trax machine is narrower than either as a statechart, and wider as a system: the server stores every instance,
checks every event against the caller and the input, and applies the transition itself.

## Statechart features

| | Trax | XState | Stateless |
|---|---|---|---|
| Authoring | C# fluent builder | TypeScript or JavaScript config with `setup()` | C# fluent `Configure` |
| States | Flat: one enum | Nested (parent) states | Substates (`SubstateOf`) |
| Parallel regions | No | Yes | No |
| History states | No | Shallow and deep | No |
| Entry and exit actions | No: a reducer on the transition | Yes | Yes |
| Delayed transitions | One timer per state (`After`), fixed or read from the context within a min and max, fired by the server from a stored due time | `after`, fired by the running actor (6.0 stores pending timers in the snapshot) | No |
| Child work | One train or one child machine per state, with done, failed and cancelled outcomes | Any number of invoked or spawned actors | No |
| Guards | Declarative rules that travel as data; a custom guard is bound by name and written once per runtime | JavaScript functions | C# delegates |
| Visual editor | No. The operations console draws a machine you wrote, and `trax machine show` exports Mermaid or DOT | Stately Studio | DOT and Mermaid export |
| Data per state | Each state declares its context as a C# record, with rules checked on every move and on every load of a stored snapshot | Compile-time types in 5; runtime schemas per state in 6.0 | No: the machine holds only the state |
| Final states | `Ends()` and `Fails()` give a parent machine its done or failed outcome | `type: 'final'` with `output` | No |
| The machine as data | Exported to a JSON definition with its guards and reducers | `serializeMachine` in 6.0 | `GetInfo()` |
| A refused event | A typed result code from one set shared by every runtime and the GraphQL schema; the engine never throws | Ignored; `can()` checks ahead | Throws by default; `OnUnhandledTrigger` overrides |

Trax machines are flat on purpose. Nested regions were considered and rejected in
[ADR 0052](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md):
a state spawns a child machine instead.

## Running a machine for real users

| | Trax | XState plus your backend | Stateless |
|---|---|---|---|
| Server runtime | ASP.NET Core on .NET 10 | Any JavaScript runtime | Any .NET |
| In the browser | The same machine, through `@traxsharp/state-machine`; it predicts, the server decides | The same machine | No |
| Who applies a transition | The server: the client sends an event, and the server reruns the transition from the stored snapshot | Yours to build | Yours to build |
| Event input | Checked against a C# record's schema in every runtime; unknown fields refused | Yours (types are compile-time only) | Yours |
| Who may send an event | Declared per event and for start (`Requires`, `RequiresPolicy`, `AnyAuthenticated`); reads, pushes and discards are owner-only at least; a rule left out stops the host | Yours | Yours |
| Storage | Built in, on Postgres or SQLite | `getPersistedSnapshot()`; storage is yours | A state accessor; storage is yours |
| Concurrent writers | A concurrency token on every write; a conflict is a typed result | Yours | One thread per instance; the rest is yours |
| Irreversible side effects | `RunsOnce<TEffect>()`: a lease, a fence token and an idempotency key; recorded once, called again only under the same key after a crash | Yours | Yours |
| Changing a machine with live instances | `Version` and `MigrateFrom` | `migrateSnapshot` in 6.0; yours in 5 | Yours |
| API and client | A GraphQL mutation per event and a live subscription; a typed React client from `trax client`, and a typed C# view from `trax machine generate` | Yours | Yours |
| A retried request | Every event, start and discard carries a request id; a retry gets the first answer back | Yours | Yours |
| Saving a half-filled form | `save` stores a whole snapshot the server validates, on machines with no server-side behaviour, and never out of a committed state | Yours | Yours |
| Who owns an instance | Each user's instances are theirs alone; a system-owned machine is started and moved only by host code | Yours | Yours |
| Limits | 50 drafts and 10 running invoked trains per user by default; a 64 KiB snapshot | Yours | Yours |
| Abandoned instances | Drafts idle 30 days, or older than 90, are swept | Yours | Yours |
| Work a state starts | A train run, queued in the same transaction as the move, recorded like any run, cancelled when the state is left, its outcome applied on any host | Invoked actors in the running process | Yours |
| History of an instance | Every write records its transition, its command and who made it: a user, host code, a timer or a train's outcome | Yours | An `OnTransitioned` hook |
| Operators | The operations console: each machine drawn with instance and stuck counts per state, each instance's history and the runs it started, lookup by id | The Stately Inspector, for one running system | No |
| An outdated client | Refused as `CLIENT_OUTDATED`; a prediction that differs from the server's result is refused as `CLIENT_DIVERGENCE` and counted | Yours | Not applicable |
| Metrics | A `Trax.StateMachine` meter: divergence, failed timers, abandoned effects | Inspection events | No |
| Clients in other languages | A .NET client; a Rust engine and client held to the same kit, not published yet | JavaScript only | Not applicable |
| Checks before it runs | `Build()` and host startup refuse an unsafe machine; `trax machine check` fails on generated files that drifted | Not found | No |
| Testing | `Trax.StateMachine.Testing`: conformance checks and a differential corpus | Path generation from `xstate/graph` | No |
| Same answers in every runtime | Shared conformance fixtures run against the C#, TypeScript and Rust (unpublished) engines | One runtime | One runtime |

The XState 6 guide to [backend workflows](https://stately.ai/docs/xstate/v6/backend-workflows) lists per-instance
locking, idempotency keys and migrations as the application's job. That list is roughly what Trax does for you.

## When to choose XState

- Your backend is Node or another JavaScript runtime. Trax's server is ASP.NET Core only.
- You need a real statechart: nested states, parallel regions, history, entry and exit actions, or several timers or
  children in one state.
- The machine is UI logic that never needs a server's verdict: a form wizard, a media player, a drag interaction.
- You want to design machines in a visual editor with people who do not write code.

## When to choose Stateless

- You need a state machine inside one .NET service: a device protocol, a domain object's lifecycle, a parser. There
  is no client, no row per instance and no caller to check.
- You want substates and entry and exit actions in C# with no infrastructure. The Trax engine runs in process, but
  storing and serving a machine needs Trax.Effect and a database.

## When to choose Trax

- A client proposes and the server has to decide: each event checked against the caller and its input, a stored
  snapshot that two writers cannot both overwrite, timers the server fires, one side effect recorded once, and a typed
  API and client generated from the machine.
- The clients are untrusted: a browser, a script or an agent. A retry must not apply an event twice, and each user may
  touch only their own instances.
- A state starts long work that has to survive a crash and leave a run record.
- Operators need to see where every instance is and what it waits on, without writing an admin page.
- The clients are .NET as well as TypeScript.

## Using them together

XState can still drive a page's own UI state while the business process lives in a Trax machine on the server: the page
sends the Trax machine's events and renders its snapshot. Stateless can run inside a junction, where a train needs a
small in-process machine for one step.
