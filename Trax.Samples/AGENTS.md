# Trax.Samples

Runnable sample applications and project templates, one per deployment topology. It sits
last in the dependency order and references everything. Bookworm is the flagship.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| a new sample, or an E2E factory's connection string | [0001](./docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md), a factory CI does not provision reports green while testing nothing |
| a guard fixture, here or upstream | [0002](./docs/adr/0002-the-samples-adopt-the-guards-as-a-consumer-would.md), Bookworm is the only place the fixtures are adopted across a real PackageReference |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Fourteen name `samples`: executable guards, exact version pinning, the
dependency direction, the three test conventions, the canonical train name, the documentation
lints, test frameworks staying out of shipped libraries, exemplars declared by attribute, Trax
owning its vocabulary, tests owning their timeouts, every `PackageVersion` naming a referenced
package, and a chain being a declaration (`0016`). Once the samples consume a Trax.Mediator with
the startup chain check, every sample train's `Junctions()` must satisfy it for its host to
start; until the pins move to that version, nothing here enforces it. In a workspace checkout the index is at
`../Trax.Docs/adr/README.md`; that path does not resolve on GitHub, because it crosses a
repository boundary.

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were real alternatives), it takes five steps and
the build enforces four. The `adr-guard` job runs on every pull request.

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

`tests/Trax.Samples.Tests.Meta/` holds twelve convention guards. Eleven are shared with other
repos; `E2EDatabaseProvisioningTests` is this repo's own and reads the CI workflow, checking
every sample factory's *default* connection string against the ports and databases CI actually
creates. A factory that declares none, because its sample runs on SQLite or the in-memory
provider or reads the connection from configuration, gives it nothing to check.

This repo has no `PublicApiSurfaceTests`, which is right: the samples are applications, and
the one package it does ship, `Trax.Samples.Templates`, is template content with no API
surface. It has no `TraxPinLockstepTests` either, and that one is not settled. That guard
checks the pins a *consumer* declares, not the packages a repo ships, and with 29 `Trax.*`
pins across six families in `Directory.Packages.props` this is the largest consumer in the
workspace. Nothing currently catches a half-bumped family here.

`tests/Trax.Samples.Tests.Reflection/BookwormArchitectureGuards.cs` is the consumer adoption
path for the shipped guard packages, and has no test bodies by design.

The census is on `tests/Trax.Samples.Tests.Meta/`, the folder the `adr-guard` job passes as
`--census-root`: every class there whose name ends in `Tests` is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason. A new guard is unclassified until you choose, and
the build says so. Opting out is a normal answer; a reason that reads as a deferral is not.

It reaches nothing in `Trax.Samples.Tests.Reflection`, and the names there end in `Guards`
rather than `Tests`, so `BookwormDataLayerGuards` and its two siblings would be invisible to
it even if it did. Their citation of `docs/adr/0002` is voluntary; keep it that way.

## Running the samples and tests

```bash
docker compose up -d          # Postgres for the samples and the E2E suites
dotnet test
```
