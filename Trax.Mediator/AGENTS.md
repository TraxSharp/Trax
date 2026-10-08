# Trax.Mediator

Decoupled dispatch: the train bus routes by input type instead of direct injection, plus
train discovery, the registry, and train-level authorization. It sits above `Trax.Effect`
and below `Trax.Scheduler`, `Trax.Api`, `Trax.Dashboard` and `Trax.Samples`.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| `[TraxAuthorize]`, or anything in `TrainAuthorization/` | [0001](./docs/adr/0001-authorization-is-fail-closed.md), the default is fail-closed and the opt-out is a named call |
| `TrainExecutionService.QueueAsync` | central `docs/0017` (a caller's enqueue goes through the mediator), `docs/0018` (the deferred, staged enqueue) and `docs/0019` (the subject key it stamps); [0004](./docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md) for the hook's time limit |
| `TrainDiscoveryService`, `TrainBus`, `LocalRunExecutor`, or anything that runs a train by name | [0005](./docs/adr/0005-a-train-run-by-name-is-the-train-that-runs.md), a run by name resolves the named train, never the one registered for its input type |
| `QueueTrainOptions.ReplayDecisionsOf`, or the `ITrainExecutionService` overload that carries it | central `docs/0041`, a requeued run replays the decisions of the run it repeats |
| `TrainChainStartupValidator`, or `SkipChainVerification()` | central `docs/0016`, a chain is a declaration the host reads at startup |

Decisions binding more than one folder live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by folder. Thirty name `mediator`, three of them superseded. Besides the repository-wide conventions, the
ones most likely to reach a change here are `0016` to `0019` (routed above) and `0007`, the canonical train name being the interface FullName, which
`InterfaceFullNameInvariantTests` in this folder enforces at the point of registration, and `0040`/`0041`,
which a train's decision steps and a requeue's replay link follow. `0042` covers how this
folder references and releases with the others. The index is at
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

`tests/Trax.Mediator.Tests.Meta/` holds sixteen convention guards. Thirteen are shared with
other folders. Three are this folder's own: `InterfaceFullNameInvariantTests`, which pins the
canonical-name rule at registration, `DICompositionSmokeTests`, which fails at the
registration point rather than at first use downstream when a wiring change drops a
required service, and `SubjectKeyGoesThroughCreateTests`, which keeps every queue entry's
subject key going through `WorkQueue.Create` (`docs/0019`).

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

`dotnet test` at the root resolves `Trax.Mediator.slnx`, which includes the Postgres
integration suite, so the root command needs a database. That suite has no skip guard: its
fixture reads `tests/Trax.Mediator.Tests.Postgres.Integration/appsettings.json` with
`optional: false` and connects in `[OneTimeSetUp]`, so with nothing listening all six of its
fixtures error rather than skip. Everything else runs without one: the convention guards, the
memory-leak suite, which uses an in-memory data context, and the `Trax.Mediator.Testing`
guard tests, which are pure reflection.

```bash
dotnet test                                   # the whole solution, Postgres included
dotnet test tests/Trax.Mediator.Tests.Meta    # the convention guards alone

# Everything that needs no database, in one command:
dotnet test --filter 'FullyQualifiedName!~Trax.Mediator.Tests.Postgres.Integration'
```

This folder ships no compose file, and the root CI provides the database as a service container
(`postgres:17`, user `trax`, database `trax_data_tests` from `.github/ci/packages.json`).
Locally, bring one up from another folder: `docker compose -f ../Trax.Effect/docker-compose.yml up -d` matches the
committed connection string exactly, because its `init-databases.sh` creates
`trax_data_tests` alongside `trax` on the container's first start. Trax.Scheduler's compose
file is the same and Trax.Samples' is a superset of it. Any other Postgres works too, as long
as a `trax_data_tests` database exists: the suite creates its schema, not its database.
