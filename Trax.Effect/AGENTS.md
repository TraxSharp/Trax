# Trax.Effect

The effect layer: execution metadata, data contexts, effect and junction providers, the
state machine engine, and the Postgres / Sqlite / InMemory data providers. It sits directly
above `Trax.Core`, so a change here reaches the six repos downstream of it (Trax.Mediator,
Trax.Scheduler, Trax.Api, Trax.Dashboard, Trax.Cli and Trax.Samples) through a published
package. Trax.Core is upstream and never sees it.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| a schema change | [0001](./docs/adr/0001-schema-changes-are-hand-written-sql.md), hand-written SQL journaled by DbUp, no EF migrations |
| an enum stored in a column, or a new value for one | [0006](./docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md), a Postgres enum mapped in three places, and a new value ships before its writer |
| anything that creates a table | [0002](./docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md), which half of the split you are in |
| a new data model | [0003](./docs/adr/0003-a-model-and-its-persistent-mapping-are-a-pair.md), and [0001](./docs/adr/0001-schema-changes-are-hand-written-sql.md) for the migration it needs |
| a table for a feature package | `Trax.Docs/adr/0009`, the DDL ships in the core provider set or it never runs |
| authorization types a consumer writes | [0004](./docs/adr/0004-trax-owns-the-authorization-vocabulary.md), Trax owns the authorization vocabulary: `[TraxAuthorize]` and `[TraxAllowAnonymous]` declare it on every surface, and the server's own attribute is refused at startup |
| `work_queue.confirmed_at`, `subject_key`, or `IWorkQueuePromotion` | central `docs/0018` (a deferred enqueue is staged, and a stranded one is cancelled) and `docs/0019` (one subject's queued work runs one at a time) |
| `IEnqueueContextAccessor` | central `docs/0018`, the context flows with the async call and is null for a deferring train |
| `Metadata.FailureClass`, the `failure_class` column, or `IFailureClassifier` | central `docs/0020`, and [0006](./docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md) for how the enum is stored |
| `ServiceTrain.Run`, `SaveOutcome`, or anything on a train's terminal write | [0005](./docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md), the outcome is written on a token the caller cannot cancel |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Nineteen name `effect`: executable guards, exact version pinning, the
dependency direction, the three test conventions (FluentAssertions, no `[Ignore]`, no fixed
delays), the canonical train name being the interface FullName, the documentation lints,
feature-package tables shipping in the core provider migration set, the public API baseline,
test frameworks staying out of shipped libraries, exemplars declared by attribute, Trax owning
its vocabulary, tests owning their timeouts, every `PackageVersion` naming a referenced package,
a chain being a declaration (`0016`), a deferred enqueue being staged (`0018`), one subject's
queued work running one at a time (`0019`), and failures being classified where they happen
(`0020`). In a workspace checkout the index is at `../Trax.Docs/adr/README.md`; that path
does not resolve on GitHub, because it crosses a repository boundary.

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

`tests/Trax.Effect.Tests.Meta/` holds the convention guards. Thirteen of the sixteen are
shared with other repos and enforce workspace-wide rules: ten appear in all eight code
repos, `PublicApiSurfaceTests` in the seven that publish an API surface,
`TraxPinLockstepTests` in five and `BuilderPartialSplitTests` in three. Three are unique to
this repo: `MigrationsIntegrityTests`, `ModelPersistentPairingTests`, and
`PostgresEnumVocabularyTests`, which checks that the three Postgres enum mappings name the same
enums and that each enum's members match its migrations (`0006`).

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

`tests/Trax.Effect.StateMachine.Persistence.Integration/MigrationSchemaTests.cs` is the
model-versus-DDL drift guard and needs a live Postgres. `docker compose up -d` provides one.

This repo also **ships** guards rather than only running them, and those live outside the
census root. `src/Trax.Effect.Data.Testing/DataLayerGuards.cs` is the data-layer guard
engine: domain contexts derive the shared base, each one has a companion interface, each
owns a distinct schema, and a migration-based context has no pending model changes.
`DomainDataLayerGuardFixture.cs` next to it is the turnkey fixture a consumer subclasses to
run all four without writing a test body. `tests/Trax.Effect.Data.Testing.Tests/` is their
own suite, and `DomainDataLayerGuardFixtureSelfTest` there subclasses the fixture the way a
consumer would. Changing either file changes what every consuming repo enforces, so treat
them as published API, not as test helpers.

## Running the tests

```bash
docker compose up -d          # Postgres for the integration suites
dotnet test
```

This repo's compose file defines one service, Postgres. The RabbitMQ broadcaster suite wants
a broker at `amqp://trax:trax123@localhost:5672/` and this repo ships nothing that starts
one: CI provisions a `rabbitmq:4-management` service container, and locally the broker comes
from `../Trax.Samples/docker-compose.yml`, whose `rabbitmq` service uses the same
credentials. Without a broker only one of that file's seven tests skips itself, the one that
wraps its `StartAsync` in a reachability probe; the other six fail on connect.
