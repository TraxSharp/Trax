---
authors: [Theauxm]
areas: [tooling, platform]
status: accepted
---

# The machine toolchain runs C# in process and TypeScript by shelling to node

`trax machine` does two different things two different ways. It exports a machine's IR by
**loading the consumer's compiled assembly in process** and calling into the machine object
directly. It generates the TypeScript twin and the differential corpus by **spawning node**
against the engine's `generate-twin.mjs` and `generate-corpus.mjs`.

## Status

**Accepted.**

## Considered options

**Reimplement the TypeScript generation in C#.** Removes the node dependency and gives one
toolchain. Rejected because the twin has to agree with the TypeScript engine byte for byte,
and a second implementation of the same generator is a second thing to drift. The engine
already owns those entrypoints, so shelling out means there is one generator, not two that
must be proved equivalent.

**Parse the machine's C# source instead of loading the assembly.** Removes the version
coupling described below and was rejected because a machine is a program: its states and
transitions can be built by any code the author writes. Reading the compiled object asks the
machine what it is, rather than guessing from syntax.

## Consequences

**The CLI is version-coupled to the engine, deliberately.** It references
`Trax.Effect.StateMachine.Persistence` at compile time and loads the machine assembly into
the *default* `AssemblyLoadContext`, not an isolated one, so the machine's reference to that
package binds by simple name to the copy the CLI already has. Type identity is preserved, and
a machine built against an older engine than the CLI still unifies with the CLI's own
`IMachine` and is discovered normally. The coupling bites in the other direction, where the
machine asks for a newer engine than the CLI carries: the default context already holds that
simple name at a lower version, and two versions of one name cannot live in it.

**node is a hard requirement for half the command.** `MachineGenerator` asks
`INodeRunner.IsAvailable()` at the top of `RunNode`, and the check is a `node --version` spawn
rather than anything memoised, so it happens once per node step rather than once per command:
`generate --twin-out X --corpus-out Y` spawns it twice. Without node the IR export still
works and the twin does not.

**node is found on PATH, never in the current directory.** The tool runs inside checkouts it
does not trust, and .NET starts a bare `node` from the application's or the current directory
before PATH, so `ExecutableLookup` resolves it from PATH's absolute entries and the process
starts from that full path. `--node` takes an absolute path or a name looked up the same way.
On Windows it runs only a `.exe` or `.com`: a `node.cmd` or `node.bat` shim runs through
`cmd.exe`, which parses its arguments again by rules no quoting from .NET survives, so the
lookup skips a shim, refuses one that is all `PATH` holds, and refuses a `--node` naming one.

**The engine's generators ship inside the tool.** Shelling to the engine's entrypoints once
meant a Trax checkout and its `node_modules` beside the tool. Packing Trax.Cli now bundles
`generate-twin.mjs` and `generate-corpus.mjs`, with the engine compiled in, to plain
JavaScript under `engine/` in the package, so an installed tool needs node and nothing else.
They are still the engine's own generators, built from the same commit, so there is still one
generator. The tool's engine and the consumer's are separate installs, so a twin they write
with specifier imports records the IR format it was generated for, and the installed
`@traxsharp/state-machine` refuses a format it cannot read on load. The package's version is no
measure: every release bumps it, mostly without touching the IR. `--engine-src` points the tool at a checkout's `tools/` instead, for work on the
engine itself.

**The consumer's own dependencies resolve from their assembly's directory**, since the CLI
loads code it did not build. The `Trax.Effect.*` assemblies in the CLI's own closure are
outside that, because they are already loaded, which is exactly what keeps type identity
intact; a `Trax.Effect.*` package the CLI does not reference resolves from the machine's
directory like any other dependency. How an upward mismatch surfaces is
not settled from the source and has not been observed: the only `catch` wraps
`LoadFromAssemblyPath` and covers `BadImageFormatException` and `FileLoadException`, while
`DiscoverMachines` calls `Assembly.GetTypes()` outside it, where an unbindable reference would
fail instead. The "no machines found" error carries a hint naming the engine package, but that
message is the empty-assembly path, not a demonstrated mismatch path.

**The node boundary is behind an interface**, so the orchestration, staging and drift logic
are unit-testable with a fake that writes canned files, and only the byte-parity tests
spawn a real **node** process. `NodeRunnerTests` spawns real processes too, using `dotnet` as
a stand-in so the suite does not require node.

## Exemplars

- `MachineLoaderTests` pins the in-process half: discovery finds only real machines,
  selection handles the single, named, ambiguous and unknown cases, and the file errors are
  the ones a user would act on. A wrong pick generates the wrong machine's artifacts.
- `NodeRunnerTests` pins the shelling half.
- `ExecutableLookupTests` pins where the node it shells to comes from: a `node` planted in the
  current directory never runs, and on Windows a batch-file shim never runs either.

Not covered: nothing checks the two halves agree. The differential corpus is what proves the
C# and TypeScript engines behave the same, and it is generated by this tool rather than
verified by it. Nothing exercises a real engine-version mismatch either;
`MachineLoaderTests` approximates it with an assembly that holds no machines, and says so in
its own comment.

## Changelog

- **2026-10-13**: On Windows node runs only from a `.exe` or `.com`, never a batch-file shim.
- **2026-10-13**: A specifier twin records the IR format it was generated for, which the
  installed engine checks on load, rather than the tool's version.
- **2026-10-13**: node is resolved on PATH by the tool, not by the OS from the current
  directory.
- **2026-10-13**: The generators are bundled into the package at pack time; `--engine-src`
  became an override.
- **2026-09-11**: Corrected two consequences. The node availability check runs once per node
  step, not once per command, and the default load context binds a machine to the CLI's own
  engine copy, so an older-engine machine unifies rather than vanishing as "no machines
  found"; the upward mismatch is unobserved and untested.
- **2026-09-11**: Recorded.
