# Trax.Samples

Runnable sample applications and project templates, one sample per major Trax feature (ADR
0006). It sits last in the dependency order and references everything.

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
| a template's `Program.cs` or its dashboard | [0003](./docs/adr/0003-templates-serve-the-dashboard-only-in-development.md), the dashboard and the demo key exist only in Development |
| a template's package versions | [0004](./docs/adr/0004-the-template-package-carries-its-package-versions.md), versions are generated at pack from the central pins |
| the README's feature-coverage table, or a new or retired sample | [0006](./docs/adr/0006-one-sample-per-major-feature-proven-end-to-end.md), one sample per major feature, each proven by E2E tests; a feature without a sample is proven upstream and listed in the table |
| `docker-compose.yml` | [0005](./docs/adr/0005-sample-infrastructure-listens-on-loopback-only.md), every published port binds `127.0.0.1` because the credentials sit beside it |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Fifteen name `samples`: executable guards, exact version pinning, the
dependency direction, the three test conventions, the canonical train name, the documentation
lints, test frameworks staying out of shipped libraries, exemplars declared by attribute, Trax
owning its vocabulary, tests owning their timeouts, every `PackageVersion` naming a referenced
package, a chain being a declaration (`0016`), and a demo credential carrying the
`do-not-use-in-production` marker and existing only in Development (`0035`). Once the samples consume a Trax.Mediator with
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

`tests/Trax.Samples.Tests.Meta/` holds twenty-one convention guards. Twelve are shared with other
repos, and nine are this repo's own. `E2EDatabaseProvisioningTests` reads the CI workflow,
checking every sample factory's *default* connection string against the ports and databases
CI actually creates; a factory that declares none, because its sample runs on SQLite or the
in-memory provider or reads the connection from configuration, gives it nothing to check.
`WorkflowJobFileAccessTests` checks that a workflow job reading a repository file checks the
repository out. `ComposePortsBindLoopbackTests` checks that every port `docker-compose.yml`
publishes is bound to `127.0.0.1`. `DemoKeysCarryTheMarkerTests` checks that every demo
credential a sample registers carries the `do-not-use-in-production` marker.
`RabbitMqCredentialsMatchComposeTests` keeps the samples' RabbitMQ credentials in step with the
user `docker-compose.yml` creates. `FeatureCoverageTableTests` checks the README's feature-coverage
table: every class it names in this repo exists in the file its row links, and every E2E project
is named by a row. Rows naming another repo's class are checked for shape only.
`SampleDatabasesAreSeparateTests` keeps every sample on a database of its own that
`docker-compose.yml` creates, since a scheduler fails another sample's manifests in a shared one.
`RabbitMqExchangePerSampleTests` requires every RabbitMQ sample to name an exchange no other sample
uses. `SamplePortsTests` keeps every sample port in 5200-5299 or 5310-5319 and owned by one sample.

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
