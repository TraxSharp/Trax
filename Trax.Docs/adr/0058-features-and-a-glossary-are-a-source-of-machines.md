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
  does not say `anyAuthenticated`, and two phrases that make the same step. Phrases are compared as the step they
  make, so two machines may share an action word their nouns tell apart. A step two machines could own is
  ambiguous, never resolved to the first.
- **Features name their people, and an actor's own act grants access.** A scenario names each person from an actor's
  noun (`Given Ada is a passenger`) and refers to a user's instance by its owner (`Ada's booking`, or `her booking`
  for the step's own person, and no one else). `When Ada gives passport "X1234567" for her booking` grants passengers
  that trigger. An actor no scenario shows doing it is denied. The glossary writes a trigger's phrase without the
  person, with `{subject}` where the machine goes (`gives passport {passport} for {subject}`), and no glossary phrase speaks as "I". Derivation also writes `access.txt`, one
  sorted line per trigger, start and view with the actors it admits, and it is committed, so a new grant shows in a
  pull request's diff. Its check fails when the file is stale.
- **Ownership is one of two kinds, and strict.** A machine is `ownedBy` one actor or a list of them, or `system`. A
  user-owned machine is moved by its owner on their own instances, and by the server (timers, train outcomes,
  policies). With several owning actors, each person owns their own instances whichever actor they are, and each
  trigger is granted to the owning actors the scenarios show firing it, `start` to those shown starting one and `read`
  to all of them; a rule whose roles would also admit an owning actor a scenario shows refused is a derivation problem,
  never a wider grant. A system machine is moved only by the server. It is either one instance at a fixed key, which
  the host starts and a served view reads, or `keyedBy` its key's parts, one instance per key, which host code starts
  and moves by the machine's id; every trigger of a keyed one is the server's, and a view reading one, a policy moving
  one or set off by one, and a person firing one are refused. A feature in which one actor acts on another's
  instance, or a person fires a system machine, is a derivation problem, until shared instances give a machine more
  than one participant.
- **A policy is the server acting.** It runs as a host actor, never as an instance's owner, on a trigger no client is
  served. Three shapes: user to user, where the target's context holds the source instance's id; system to users,
  every instance in the waiting state, in capped batches; and user to system, a system machine at a fixed key. A
  trigger that features show both a person and a policy firing is refused, naming both scenarios. Policies that set
  each other off with no timer between them (counting a train's outcome as a move the invoking state causes) are
  refused by derivation and by the host at startup, through one check, naming the policies; a loop a timer closes is
  allowed.
- **An invoked train may take the instance's owner as input, set by the server.** A state's `invokes` maps a train
  input field to `$owner`, and the server fills it with the owner's qualified principal id (`{scheme}:{id}`) when the
  state is entered. It is in no context, so no client writes it, and a save that puts it there is refused as any
  unknown field is. A system machine has no owner, so `$owner` there is refused, as is a context field of that name.
  The mapping is copied to `host.json`. A train acting for a person then takes who they are from the machine rather
  than from a field the client set.
- **Trax serves the views, under a derived read rule.** A view is read by the actors whose scenarios read it, and by
  no one else. It reads only the caller's own instances and system instances at their fixed keys, and shows only the
  fields the glossary lists. Each user machine a view reads also holds the caller to its own read rule, as its `get`
  does, so a view never shows an instance its machine would refuse to; a machine that refuses the caller reads as no
  instances, and the rest of the view still reads. A view no scenario reads is not served.
- **Features are read by Cucumber's own parser,** in a derivation package of its own, so the engine takes no new
  dependency. A value in a step is quoted and read as its field's type (`When Ada checks in for her booking in seat "14C"`). The fixed steps
  (`Ada is told "..."`, `Ada is not allowed`, `5 minutes pass`, `still`) are English only in this version.
- **Discard follows read.** A derived machine's owner discards their own instances, narrowed by the read rule's roles;
  committed, invoking and effect-target states are not discarded whatever the rule.
- **A trigger the server fires is not a client's.** A policy's trigger is never served, and a trigger no scenario
  fires, or one only ever denied, is not served at all.
- **The glossary says a machine's shape as the IR holds it.** A machine's `version`, each state's `committed`,
  invariant (`requires`) and own `context`, and `differential: true` for the IR's `differential` block are glossary
  keys, written into the IR as the fluent exporter writes them, so a derived machine can carry what a hand-written one
  does.
- **The C# behind a machine is named in the glossary and bound by the host file.** A run-once effect, a custom rule or
  reduction and a migration are C# the IR names but cannot carry. The glossary names each type; derivation writes what
  clients see (the custom names, an effect's key prefix) into the IR and the types into `host.json`, and the host finds
  each by its full name in the assemblies it serves from, pairing them one for one with the IR: a name with no type, or
  a type for nothing, refuses the host. Derivation and an in-memory run load no assembly, so they play the example the
  glossary gives each, and a scenario that needs the real handler is tagged `@host`, read but not derived from, and run
  only against the host.
- **The Reqnroll bindings run the same features against a host, and a test project may add steps.** Each step is read
  as derivation reads it; a step no glossary phrase matches may be one the test project registers through
  `TraxFeatures.Configure`, never with Reqnroll's own attributes, and the glossary always reads first, so a registered
  step never changes what a glossary step means. A feature in a folder named `features` is a machine feature, where a
  Then nothing reads is about the UI and pending; any other (a sample's go in `specs/`) is a spec, where such a Then
  fails. Given the host's client manifest, the bindings send every start, trigger and view read by its persisted id,
  as a page does, so the features run against the host's shipped posture.
- **Fail closed everywhere.** No rule means not served. Two scenarios that disagree, or two grants that clash, are a
  derivation problem naming both scenarios, never a rule widened to fit.
- **The parser checks itself.** Derivation replays every scenario on the machines it derived and fails on any that
  does not pass, so it never writes a machine its own features fail. Gherkin it cannot express (data tables,
  DocStrings, `*` steps) and a Given or When it cannot resolve are refused. A scenario is identified by file and line,
  never by its name. Timers that go round a loop of states with no event leading out are refused at derivation, naming
  each scenario that showed one of them, by the same search the host's builder refuses them with.
- **The Thens after a When follow the cascade in order.** The first about the acting instance says where the When
  took it; one naming it in another state, after another instance moved, says where a policy took it on. The replay
  and the host bindings accept a Then naming a state the instance entered since its last move that was not a
  policy's.

## Considered options

- **Three files per machine** (IR, access, policies). Rejected: policies and views span machines and had no natural
  home, and every pair of files needed its own pairing check.
- **One bundle per machine holding the IR and the host parts.** Rejected: the IR stops being a standalone document, and
  anything that ships the file whole ships the access rules to the client.
- **Features in the first person ("I").** Rejected: "I" cannot name two people in one scenario, so one actor acting on
  another's instance stays invisible until the server refuses it. Cucumber's published advice (real data, named
  people) points the same way.
- **Grants only through explicit "can" lines.** Rejected: every trigger would be written twice. Implicit grants with
  no report were rejected too, since an access change would hide among policies, views and provenance in `host.json`.
- **Clients may fire role-gated system instances.** Rejected: it brings the questions of several users on one
  instance (whose run cap, which actor is recorded) into this design without answering them.
- **One hand-written query per view.** Rejected: the role is typed by hand and checked against nothing, and forgetting
  it serves the view to any signed-in caller.

## Exemplars

**Enforced elsewhere:** in `Trax.StateMachine.Tests`, `GlossaryFormatTests` (versions, unknown keys, role-less
actors, duplicate phrases, a phrase that speaks as "I"), `HostFileFormatTests` (unknown keys, owners named by actor, a policy firing a client's
trigger, a client rule on a system machine), and `FormatCorpusTests`, which holds both readers and both schemas to the
corpus under `Trax.StateMachine/conformance/formats/`, as `formatSchemas.conformance.test.ts` does for the schemas.
Serving from the IR is held by `MachineFromIrTests` there (each conformance machine a file can describe exports exactly
its file and replays its corpus; what a file cannot describe, a mispaired host entry and access that disagrees with the
IR's timer rules are refused), `MachineDirectoryTests` there (a directory's host file and IRs list the same machines and name only what the IRs have, and its policies set each other off only through a timer), `IrMachineHostTests` in `Trax.StateMachine.Persistence.Integration`, `EffectBindingTests` and `MigrationBindingTests`
there (an effect, custom handlers and a migration the host file names, run as named), and
`IrMachineSchemaTests` and `IrMachineE2ETests` in Trax.Api. Derivation is held by `GherkinDerivationTests` there (each refusal, the self-check, and a derived machine served as
written), `AccessDerivationTests` (grants from an actor's own act, nothing widened, the access table) and
`TimerDerivationTests` (time on a scenario clock, stays, timer rules the host accepts), `GlossaryStateShapesTests`
(versions, committed states, rules and contexts per state, the differential block) and `HandlerDerivationTests` (the
C# a machine names, the examples an in-memory run plays, `@host`), and policies by
`PolicyDerivationTests` (the three shapes, what a policy cannot be, a loop with no timer in it) and `PolicyRunnerTests` in
`Trax.StateMachine.Persistence.Integration` (the owner's linked instance only, the fire recorded as the policy's, once
across hosts and restarts, a fire left for the host that serves its policy and target, a broadcast capped and coalesced,
a server-only trigger refused to a client). Views are held by
`ViewDerivationTests` (readers from the people who read a view, a person's own instances only) and
`IrMachineViewsE2ETests` in Trax.Api (a reader's own rows, `FORBIDDEN` for anyone else, no rows of a machine whose
read rule refuses the reader, the system instance started by the host), and their generated persisted documents by `MachineViewDocumentsTests` there (the text sent raw refused, the
document run by its id, the read rule still applied). `ProvenanceDerivationTests` holds the scenarios behind each part
(by file and line, in a stable order) and `CascadePolicyTests` a chain's moves read from the chain. Running features is
held by `ScenarioRunnerTests` (in memory, answering as the host does), `GapStubTests` (a gap's stub green when pasted),
`FeatureMachinesTests` (the package's public calls), `ScenarioDriverParityTests` in Trax.Api (the same scenarios answer
the same in memory and against a host) and `FromGherkinCommandTests` in Trax.Cli (the directory the command owns).
`ServeFeaturesTests` in `Trax.StateMachine.Persistence.Integration` holds a host serving that directory with one option.
Ownership and keys are held by `MultiActorOwnershipTests` (each trigger to the owning actors shown firing it, no rule
widened) and `KeyedSystemMachineTests` (one instance per key, no person, view or policy on one) in
`Trax.StateMachine.Tests`, `MachineInstancesByIdTests` in `Trax.StateMachine.Persistence.Integration` (host code
starting and advancing a served machine by its id, a user-owned one refused) and `InvokedOwnerInputTests` there
(`$owner` as the owner's principal id, never in a context). `CustomStepsTests` in Trax.Api holds the bindings' own
steps (the glossary first, a registered step acting as the scenario's person, a spec's unread Then failing).
The board is held by `BoardTests` and `InvokedTrainBoardTests` (stickies from the files alone, an invoked train's
outcomes) and `MachineBoardE2ETests` in Trax.Api (`operations.machineBoard`, behind the operations gate).

Not covered: features run in memory and against a host, never through a page. A page's browser tests are written by
hand, so nothing checks that a page tells the same story as its features.

## Changelog

- **2026-11-09**: Amended by [0059](./0059-the-lexicon-is-the-source-and-the-glossary-is-derived.md): a team writes the lexicon, and the
  glossary is derived from it and committed in `machines/`.
- **2026-11-09**: The console draws the board on a Board page of its own, under Overview, as paper notes on a wall.
- **2026-11-08**: The CheckIn sample replaces the Bakery as the worked example, in this record and the pages that
  follow it.
- **2026-11-08**: The Thens after a When follow the cascade in order, so a scenario can show a policy taking the acting
  instance on.
- **2026-11-08**: Derivation refuses timers that loop with no event leading out, as the host's builder does.
- **2026-11-08**: The Reqnroll bindings take a test project's own steps, fail a spec's unread Then, and send by
  persisted id when given the host's manifest.
- **2026-11-08**: An invoked train may take the instance owner's principal id as `$owner`, set by the server.
- **2026-11-08**: A system machine may be `keyedBy` its key, one instance per key, started and moved by host code.
- **2026-11-08**: `ownedBy` may list several actors, each trigger granted to the owners shown firing it.
- **2026-11-08**: The glossary says a machine's version and each state's shape, and names its effect, custom handlers
  and migrations, which the host file binds.
- **2026-11-08**: A view returns at most the newest `ViewRowLimit` instances of each user machine.
- **2026-11-08**: A view holds each user machine it reads to that machine's own read rule.
- **2026-11-08**: A host refuses policies that set each other off with no timer between them, as derivation does, through one check.
- **2026-11-07**: The Bakery sample: machines written only as features, run in memory and against its host.
- **2026-11-07**: The console's Board tab draws a machine's board from `operations.machineBoard`.
- **2026-11-07**: Reqnroll bindings run features against a host, and the Gherkin package has public calls.
- **2026-11-06**: Persisted documents for views: a host that requires persisted operations serves them.
- **2026-11-06**: The CLI's `trax machine from-gherkin`, `run`, `missing`, `usage` and `diagram`.
- **2026-11-06**: Features run in memory as the host answers; missing scenarios are listed with stubs checked green.
- **2026-11-06**: Views land: derived readers, the `views` root, system instances started by the host.
- **2026-11-06**: Policies land: server-only triggers, the fire table, the runner, derived policies.
- **2026-11-06**: Derivation, access and timers land; their tests named in Exemplars.
- **2026-11-06**: Features name their people in the third person; "I" is refused.
- **2026-11-06**: Recorded.
