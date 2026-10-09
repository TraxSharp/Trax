# Trax.Cli

The `trax` command-line tool: scaffolds Trax projects from a GraphQL or OpenAPI schema, and
generates state-machine artifacts (IR, TypeScript twin, differential corpus) from a compiled
machine. It sits after `Trax.Scheduler` in the dependency order, and the declared order puts
`Trax.Samples` downstream of it: `DependencyDirectionTests` lists `Trax.Cli` among Samples'
allowed upstream. No other folder references a `Trax.Cli` project today, so in practice nothing
depends on it yet.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| anything under `Machines/` | [0001](./docs/adr/0001-the-machine-toolchain-is-half-in-process.md), the C# half loads a compiled assembly and the TypeScript half spawns node |

Decisions binding more than one folder live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by folder. Twenty name `cli`, three of them superseded: executable guards, the
dependency direction, the three test conventions, the documentation lints, the public API
baseline, test frameworks staying out of shipped libraries, property tests using CsCheck in test projects only (`0044`), exemplars declared by attribute, Trax
owning its vocabulary, tests owning their timeouts, every `PackageVersion` naming a referenced
package, a chain being a declaration (`0016`), and one repository releasing at one version
(`0042`). The index is at
[`../Trax.Docs/adr/README.md`](../Trax.Docs/adr/README.md).

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were real alternatives), it takes five steps and
the build enforces four. The root CI's `adr-guard` job checks this folder on every pull
request that changes it or a folder upstream of it.

| | Step | Enforced |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | no, this is the human step |
| 2 | Tag it `areas`, and add it to `docs/adr/README.md` | yes |
| 3 | Say where it stands in `## Status` and record it in `## Changelog` | yes |
| 4 | Give it `## Exemplars`: guards, `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | yes |
| 5 | Have each guard you named cite the ADR back, in its docstring and its failure message | yes |

Step 1 is the only one you have to remember, because no test can detect a decision you chose
not to record. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](./.claude/skills/recording-decisions/ADR-FORMAT.md).

## Guards

`tests/Trax.Cli.Tests.Meta/` holds eleven convention guards, and **all eleven are shared** with
the other folders. This folder owns no convention guard of its own.

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

```bash
dotnet test
```

The machine unit tests drive a fake `INodeRunner` and spawn no process at all. Only the
byte-parity integration test spawns real node, and it skips itself at runtime when node is
not on PATH, which is what lets the suite run without node installed. `NodeRunnerTests` does
spawn a real process, using `dotnet` as a stand-in executable.
