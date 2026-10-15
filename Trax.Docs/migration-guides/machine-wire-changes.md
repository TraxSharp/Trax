---
title: Adapt to the machine wire changes
description: "Upgrade steps for the release that made refusal codes one enum, typed each machine's push, spelled machine enums as stored, and let starters follow runs."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 0
---

# Adapt to the machine wire changes

Applies from: Trax 1.64.0.

This release changes what a machine client sends and reads, once, so that a client can be generated from the host's
schema. Every client of a machine is out of date after it: regenerate twins, update the code below, and deploy the
clients with the hosts.

## Refusal codes are one enum

Every code a machine operation is refused with is a member of `RefusalCode`, in C#, in the GraphQL schema and in
`@traxsharp/state-machine`, and travels upper snake case: `guard-failed` is now `GUARD_FAILED`, `client-outdated` is
`CLIENT_OUTDATED`. That holds for `problem.code`, for a persisted machine document's `extensions.code`, and for the
engines' own rejections. See [Result codes](/docs/sdk-reference/statemachine-api/result-codes).

- **C#.** `RejectionReasons`, `RehydrationErrorCodes` and `DraftRefusalCodes` are gone; use `RefusalCode` members
  (`RefusalCode.GuardFailed`). The `Rejected` and `Error` records and `SnapshotProblem.Code` are typed `RefusalCode`,
  so a host can no longer refuse with a code of its own. Where a string is needed, `code.ToWire()` gives the wire
  spelling.
- **TypeScript.** `RejectionReasons` and `RehydrationErrorCodes` are gone; import `RefusalCode`
  (`RefusalCode.GuardFailed`). The codes the package reports itself are `ClientProblemCode`: `GONE`, `NOT_READY`,
  `TRANSPORT_ERROR`, `NOTHING_TO_RETRY`, `DISPOSED`. Compare `problem.code` against those constants, not string
  literals: a literal such as `'gone'` no longer matches anything, and the type now says so.
- **Committed corpora** regenerate with the new spelling (`UPDATE_DIFFERENTIAL=1`, or `trax machine generate`).

## Each machine has its own push

`machineChanged(machine:, id:)` is gone. Each exposed machine has its own subscription field, named from its id,
`topicMapChanged(id:)` for `topic-map`, whose events use the machine's types: the state as its state enum, the
context as its context type, `dueAtKnown` beside `dueAt`. See [The push](/docs/sdk-reference/graphql-api/machines#the-push).

- **Persisted documents.** The push's document is registered under the operation `changed`. A client that reads the
  manifest (`createMachineServer`) needs only the new documents; a hand-written subscription document changes its
  field and its selection.
- **An event named `changed`** is refused at startup, as `start`, `discard` and `get` are.

## Machine enums are spelled as stored

An enum a machine's context, start input or event inputs hold is spelled in GraphQL by its C# member name
(`HighPriority`), not HotChocolate's `HIGH_PRIORITY`, in inputs and answers alike. A train that takes the same enum
sees the same spelling. Before, a client that sent `HIGH_PRIORITY` with a `clientResult` was refused as client
divergence, since the server stored `HighPriority`.

## The IR no longer names the effect type

An IR's effect edge is `{ "keyPrefix": ... }`; the server's effect type name no longer reaches a client. The twin
reads only that the edge is effect-bound, so the IR format version is unchanged, but the IR hash of a machine with a
`RunsOnce` edge changes: regenerate its twin.

## Every exposed train has a generated document

A host with persisted operations registers a document for every train it exposes, under `trax_train_`, with an id
that changes when the train's input or output does
([Generated documents](/docs/sdk-reference/persisted-operations/use-persisted-operations#generated-documents)). A
client sends that id and gets `CLIENT_OUTDATED` after the train changes, not a validation error.

- **Hand-written train documents keep working** while they stay uploaded; move clients to the generated ids, then
  deactivate the old ones.
- **An exposed train whose answer holds itself**, or whose answer has a field with a required argument, now refuses
  the host's start, naming the train and the type. Flatten the output, or stop exposing the train.
- **The first rolling deploy.** A node from before this release does not reserve `trax_train_`, so while old and new
  nodes overlap a client sending a new train id to an old node gets one validation error. It happens once.

## The TypeScript client sends only registered documents

`@traxsharp/state-machine` no longer ships the client-authoritative path, and its client sends only the persisted
documents a host registered.

- **Gone:** `useMachine` (from `/react`), `createSnapshotClient` and `createHttpExecutor` (from `/client`), and the
  `persist` option of `MachineController`, which now only holds a machine in memory. A machine a server holds is
  driven by `useServerMachine` or `ServerMachineController` over `createMachineServer`.
- **`createMachineExecutor`** takes each machine's `documents` (from the host's
  [client manifest](/docs/sdk-reference/statemachine-api/client-manifest)) and throws when a machine has none; its
  `context` option is gone, since each document selects the whole answer.
- **`createDraftSession` and `startDraft`** take the executor (`{ executor, machine, id }`) in place of a client.
- **Generated code checks its client format.** Code `trax client` generates calls `requireClientFormat` when it
  loads, and a package that serves another format throws, naming both.
- **`useRemembered<Machine>` takes the user** (`useRememberedCheckout({ user })`) and keeps each user's instance
  under its own key, `trax.<machine id>.<user>`, so an instance remembered under the old `trax.<machine id>` is not
  opened again. `useRememberedMachine` no longer forgets an instance the server answers `NOT_FOUND` (it answers
  another caller's instance the same way): the hook stops with `error.deleted` false, and `forget()` starts over.
  This is client format 2; the runtime still serves format 1.

## A run can be followed by the caller who started it

A train marked [`[TraxRunProgress]`](/docs/sdk-reference/graphql-api/run-progress) can be read with
`query { runProgress(runId) }` and followed with `subscription { runProgress(runId) }` by the caller who started the
run, and by nobody else. A client that followed its own runs through the broadcast feeds or with an operator
credential moves to it.

- **Runs record their starter.** Migrations 082, 083 and 084 (SQLite 042 and 043) add `starter_hash` to
  `work_queue`, `metadata` and `manifest` and run on startup. Nothing is backfilled, so a run from before the upgrade,
  or one a node on the previous version starts during a rolling deploy, cannot be followed. See
  [Run starter](/docs/reference/schema-migrations#run-starter-082-and-083-sqlite-042).
- **A scheduled run records nobody**, unless the manifest is scheduled for a caller with
  [`ScheduleOptions.StartedBy`](/docs/sdk-reference/scheduler-api/schedule). Then each of its runs, its retries
  included, carries that caller, and a failed run answers `retriedAs` with the run that retries it once that run is
  queued.
- **A machine that invokes a train** answers its user owner `invokedRun { runId }` while the instance sits in the
  state that queued the run, and the run records the owner as its starter, so the owner follows it with
  `runProgress`. A committed client regenerates to pick up the field.
- **The invoked-run cap counts by starter.** A user's run still counts after its draft expired, until the run ends.
- **Generated code.** `trax client` writes `progress.g.ts` (`readRunProgress`, `followRunProgress`,
  `useRunProgress`) when the manifest lists a train that carries the attribute. See
  [`trax client`](/docs/reference/cli#frontend-clients-trax-client).
