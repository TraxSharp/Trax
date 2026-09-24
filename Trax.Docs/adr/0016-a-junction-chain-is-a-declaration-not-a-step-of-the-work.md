---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard, cli, samples]
areas: [platform, testing]
status: accepted
---

# A junction chain is a declaration, not a step of the work

`Junctions()` names the junctions a train passes through and nothing else. The accessors for
per-execution state (`TrainInput`, `TrainOutput`) throw while a chain is being declared, so a
chain cannot vary by the value being processed. That makes every train's chain readable at host
startup, from types alone, before any of the work it describes has run.

## Status

**Accepted.**

## Why this is written down

Because the restriction looks arbitrary until you try to check a chain that does not obey it.

A chain that branches on its input does not have one shape, it has several. Reading it yields
whichever shape the reading conditions happen to select, so the chain verified at startup need
not be the chain that runs, and a green startup check would mean nothing. The value of eager
verification depends entirely on there being a single chain to verify.

The rule was also already true everywhere it mattered and simply unenforced. None of the 96
`Junctions()` overrides across the eight repos read the input when the gate was added; the only
violations found anywhere were in a consumer's generated `POST` trains, which read the input to
stamp an identifier before building the chain.

## Considered options

**A static chain declaration.** A `static abstract` interface member has no `this`, so
per-execution state would be unreachable by the compiler rather than at runtime, and branching on
ambient state such as the clock would be closed off too. Rejected for now on cost: it changes the
shape of every train in every repo, and it cannot carry the `AddServices<T>(T service)` and
`Chain<TJunction>(TJunction instance)` overloads, which take values rather than types. The
runtime gate gets the same guarantee for input-dependence at zero migration. This remains the
option to take if ambient branching ever turns up in practice.

**A Roslyn analyzer.** `TrainChainAnalyzer` parses fluent chains, so it could in principle report
a chain that reads the input. It only inspects chains rooted at `Activate()`, though, and with
`Activate` internal it no longer sees any chain a train can write; it is deprecated. Rejected as
the primary mechanism because an analyzer has to be referenced to run, which makes correctness
opt-in and silently absent wherever someone forgot.
An analyzer remains useful as a second, earlier signal; it is not what the guarantee rests on.

**Reading the chain from IL.** Inspecting the method body avoids both executing the chain and
changing its shape. Rejected as fragile across compiler versions and effectively undebuggable
when it reads a body wrongly.

## Consequences

**Work that needs the input moves into a junction.** That is where per-execution decisions
belong, and a junction receives the input as its argument. A train that wants to stamp an
identifier onto its input declares a junction that does it.

**Reading a chain resolves nothing.** Steps are recorded from type arguments, so a junction that
cannot be constructed is still declared. Whether a junction can be constructed is not checked at
all: a junction takes its constructor arguments from Memory, which the chain fills as it runs, so
the answer is not decidable from the declaration. The container is consulted only to decide
whether a junction's input can be supplied from outside Memory.

**A declaration that does work is refused.** `Junctions()` runs when a chain is read, so a body
that awaits before returning, returns a result instead of ending in `Resolve()`, or ends in
`Resolve(value)` has no chain to read. Each is recorded as a refusal and the host will not
start, instead of the train reading as an empty, clean chain. Whatever such a body started at
boot is not undone. A monad the train creates while declaring, through `NewMonad()` as much as by
the train itself, records instead of running, so reading a chain never executes a junction.

**The replay has to mirror the runtime.** A chain is only as verified as the replay is faithful.
The train's input enters Memory under its declared type and interfaces (a run stores it under its
declared type as well as its runtime type, so a junction taking the declared base type works for
any subtype), and tuple elements likewise under their declared type and interfaces (a run stores
each element under its declared and runtime types, skipping a null element). A junction's output,
an extracted value and an `AddServices` value enter under exactly one type, and lookup is by exact
type before the container, except that a tuple input is assembled from Memory alone, never the
container. A short circuit's output is not counted as available to what follows it. At runtime a
short circuit that returns Right stores its output and the chain keeps running, but one that
returns Left stores nothing and the chain keeps running too, so a later junction cannot rely on
the value. Shapes the runtime refuses on every run are refused when the chain is read: `IChain` or
`AddServices` of a class, `Chain` or `ShortCircuit` of a type that is not a junction, and a short
circuit whose output cannot be the train's return type.

**A train the check cannot build is skipped with a warning, not refused.** The check builds every
train to read its chain. A train that cannot be constructed at startup, because its constructor
needs something only a request provides, or a registered train that does not derive from
`Train<,>`, is logged as unverified and skipped. A train that cannot be built is not evidence of a
chain that cannot run, and refusing to start over one would take down a host whose trains all
work.

**A train that can be built but whose `Junctions()` throws is refused.** Any exception, not only a
`ChainDeclarationException`, fails the start and is reported as a chain that could not be read. The
common case is a `Junctions()` that dereferences `Metadata`, which is null outside a run, and fails
with `NullReferenceException`. Skipping it would pass a train whose declaration fails every time
it is read, which is exactly what a run does first.

**The refusal happens only where the check runs.** It is a hosted service `AddMediator` registers,
so it runs when a generic host starts. A host with `SkipChainVerification()`, the Lambda runner and
a bare `ServiceProvider` never run it; there, a chain that cannot run fails when something first
runs the train, as it did before the check existed.

**Branching on ambient state is still possible.** A chain that reads the clock or a static flag
records whichever shape boot-time conditions select, with no signal that it did. Only
`TrainInput` and `TrainOutput` are gated; branching on `Metadata`, an injected property or any
other instance state is equally unchecked. Closing that needs the static declaration above.

**`RunInternal` and `Activate` are no longer reachable.** A train that built its chain
imperatively could not be read, so the escape hatch and the guarantee could not both exist.
`RunInternal` is private and `Activate` is internal, which means `Junctions()` is the only way to
declare a chain and every train is therefore readable. Closing them cost four `Chain` overloads
and a parameterless `Resolve()`, added so the declaration can express what the imperative form
could.

**Closing them ships as a minor release, and that breaks scaffolded projects.** The commits that
remove them are `feat:`, so Semantic Release cuts a minor, not a major. The cost lands on projects
that float: `trax generate` from earlier Trax.Cli versions wrote trains overriding `RunInternal`
into a project referencing Trax with `Version="1.*"`, so such a project stops compiling on its next
restore with nothing changed in it. Trax.Cli therefore releases its `Junctions()` template before
Trax.Core and Trax.Effect release the removal. `Junctions()` already exists in the released
packages, so a project scaffolded by the new Cli compiles against both, while one scaffolded by the
old Cli breaks whichever order is chosen; the order only keeps the current Cli from generating code
that does not build. The upgrade note is
[Enqueue and Outcome Changes](/docs/migration-guides/enqueue-and-outcome-changes).

**Seeding extra inputs is gone with it.** `Activate(input, otherInputs)` had no declarative
equivalent, and the trains that used it were restructured so that a value a later junction reads
is produced by an earlier junction. `AddServices(value)` and `Extract(value)` still put a value in
Memory directly; the replay records each as a seed of its declared type.

## Exemplars

**Enforced elsewhere:** `DeclaredChainTests` in Trax.Core pins that every step is recorded in
order with its junction's input and output types, that no junction runs, that an unconstructible
junction is still declared, and that the train is runnable afterwards. `ChainDeclarationTests` in
Trax.Effect pins that reading `TrainInput` or `TrainOutput` while declaring throws, naming the
train and the member, and that a chain naming only junctions declares cleanly.
`DeclaredChainTests` also pins the refusals: an awaiting body, a direct result, `Resolve(value)`,
and an async body's exception being rethrown. `TrainChainStartupValidatorTests` in Trax.Mediator
pins that a host refuses to start when a train names a junction whose input never reaches Memory,
declares a chain that reads the input (synchronously or in an async body), does work before
declaring, or has a `Junctions()` that throws something other than a declaration error; that it
reports every failing train at once; that a container-supplied input is checked without building
the service; that a train which can only be built inside a request is skipped with a warning
rather than refused; and that the opt-out works. `SchedulerChainVerificationTests` in
Trax.Scheduler reads and replays the scheduler's own trains (the ManifestManager, the
JobDispatcher and the JobRunner), so a regression in one of them cannot stop every scheduler host
from starting. `ChainVerificationTests` in Trax.Core pins the replay itself: a junction output's
interfaces are not available and the run fails the same way, a short circuit neither supplies the
return value nor its output, seeds and tuples are available, a tuple element declared as a base
class is satisfied whatever subtype it holds, a tuple element is not taken from the container,
`Extract` ignores the container, `IChain` needs its junction, and a short circuit whose output
cannot be the result is refused. `DeclaredChainTests` also pins that `IChain` and `AddServices` of
a class are refused, that a monad created through `NewMonad()` while declaring records instead of
running, and that naming a type which is not a junction is refused rather than thrown.

Not covered: nothing detects a chain that branches on ambient state or on instance state other
than the input and output. The replay knows the train's declared input type, not the subtype that
flows, so a junction asking for an interface only that subtype implements reads as a fault; that
is why the check has an opt-out rather than being unconditional.
`Trax.Cli.Tests.UnitTests.ScaffoldCompilationTests` compiles the current template against the
Trax the Cli is pinned to; nothing checks a project an older Cli scaffolded against a new Trax
release, or that Trax.Cli ships its template before Trax.Core ships a removal.

## Changelog

- **2026-09-23**: Distinguished a train the check cannot build (skipped with a warning) from one
  whose `Junctions()` throws (refused), recorded that the refusal happens only where the hosted
  check runs, and recorded that closing `RunInternal` and `Activate` ships as a minor that breaks
  projects scaffolded by an older Trax.Cli, hence the Cli-first release order. Added
  `SchedulerChainVerificationTests`.
- **2026-09-23**: Recorded.
