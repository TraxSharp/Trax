---
authors: [Theauxm]
repos: [statemachine, api, cli, samples]
areas: [platform, graphql]
status: accepted
---

# Features and a glossary are a source of machines, and what they grant is reported

A team may write a machine's behaviour as Gherkin feature files plus one glossary that says what the words mean, and
Trax derives the machine from them: its states, triggers, guards, timers, its access, the policies between machines
and the views over them. The derived machine is served like a hand-written one, with no machine class and no
hand-written access rule. The point is that the features stop being a description of the machine and become its
source, so a scenario nobody wrote is a gap Trax can report, and a scenario that contradicts another is a build
failure rather than a bug.

## Status

**Accepted.**

## Decisions

- **One IR per machine, one host file per app.** Derivation writes `<machine>.ir.json` for each machine, in the
  existing portable IR format every engine reads, and one `host.json` holding everything server-only: each machine's
  owner and access, the keys of system instances, the policies and views between machines, and provenance. The IR
  ships to clients and its hash is in every persisted document id, so access stays out of it: a permission change is
  never a client-breaking change. The host loads the directory in one call and checks `host.json` against every IR.
- **Both formats are versioned and have a schema.** `glossary.schema.json` and `host.schema.json` sit beside
  `ir.schema.json` in `Trax.StateMachine/conformance/`. A reader refuses an unknown key, an actor with no roles that
  does not say `anyAuthenticated`, and a phrase or action word used twice. A step two machines could own is
  ambiguous, never resolved to the first.
- **A first-person act grants access, and every grant is reported.** `When I submit my ticket`, under
  `Given I am a customer`, grants customers that trigger. An actor no scenario shows doing a thing is denied. Derivation
  also writes `access.txt`, one sorted line per trigger, start and view with the actors it admits, and it is
  committed, so a new grant shows in a pull request's diff. Its check fails when the file is stale.
- **Ownership is one of two kinds, and strict.** A machine is `ownedBy: <actor>` or `system`. A user-owned machine is
  moved by its owner on their own instances, and by the server (timers, train outcomes, policies). A system machine is
  moved only by the server and read through a served view. A feature in which one actor acts on another's instance,
  or a person fires a system machine, is a derivation problem, until shared instances give a machine more than one
  participant.
- **A policy is the server acting.** It runs as a host actor, never as an instance's owner, on a trigger no client is
  served. Three shapes: user to user, where the target's context holds the source instance's id; system to users,
  every instance in the waiting state, in capped batches; and user to system, a system machine at a fixed key. A
  trigger that features show both a person and a policy firing is refused, naming both scenarios.
- **Trax serves the views, under a derived read rule.** A view is read by the actors whose scenarios read it, and by
  no one else. It reads only the caller's own instances and system instances at their fixed keys, and shows only the
  fields the glossary lists. A view no scenario reads is not served.
- **Fail closed everywhere.** No rule means not served. Two scenarios that disagree, or two grants that clash, are a
  derivation problem naming both scenarios, never a rule widened to fit.
- **The parser checks itself.** Derivation replays every scenario on the machines it derived and fails on any that
  does not pass, so it never writes a machine its own features fail. Gherkin it cannot express (data tables,
  DocStrings, `*` steps) and a Given or When it cannot resolve are refused. A scenario is identified by file and line,
  never by its name.

## Considered options

- **Three files per machine** (IR, access, policies). Rejected: policies and views span machines and had no natural
  home, and every pair of files needed its own pairing check.
- **One bundle per machine holding the IR and the host parts.** Rejected: the IR stops being a standalone document, and
  anything that ships the file whole ships the access rules to the client.
- **Grants only through explicit "I can" lines.** Rejected: every trigger would be written twice. Implicit grants with
  no report were rejected too, since an access change would hide among policies, views and provenance in `host.json`.
- **Clients may fire role-gated system instances.** Rejected: it brings the questions of several users on one
  instance (whose run cap, which actor is recorded) into this design without answering them.
- **One hand-written query per view.** Rejected: the role is typed by hand and checked against nothing, and forgetting
  it serves the view to any signed-in caller.

## Exemplars

**Enforced elsewhere:** in `Trax.StateMachine.Tests`, `GlossaryFormatTests` (versions, unknown keys, role-less
actors, duplicate phrases), `HostFileFormatTests` (unknown keys, owners named by actor, a policy firing a client's
trigger, a client rule on a system machine), and `FormatCorpusTests`, which holds both readers and both schemas to the
corpus under `Trax.StateMachine/conformance/formats/`, as `formatSchemas.conformance.test.ts` does for the schemas.
Serving from the IR is held by `MachineFromIrTests` there (each conformance machine a file can describe exports exactly
its file and replays its corpus; what a file cannot describe, a mispaired host entry and access that disagrees with the
IR's timer rules are refused), `MachineDirectoryTests` there (a directory's host file and IRs list the same machines and name only what the IRs have), `IrMachineHostTests` in `Trax.StateMachine.Persistence.Integration`, and
`IrMachineSchemaTests` and `IrMachineE2ETests` in Trax.Api.

Not covered: those hold the formats and the serving. Derivation, the access it grants, the self-check, policies and
views are held by their own tests as each lands, and this ADR's changelog names them then.

## Changelog

- **2026-11-06**: Recorded.
