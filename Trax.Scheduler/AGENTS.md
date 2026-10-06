# Trax.Scheduler

Timetable management: manifests, the work queue, retries, dead-lettering, job dependencies,
and the remote execution topologies (HTTP workers, SQS, Lambda). It sits above
`Trax.Mediator` and below `Trax.Api`, `Trax.Dashboard` and `Trax.Samples`.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| the remote run request or response | [0001](./docs/adr/0001-remote-execution-is-a-json-wire-contract.md), the shape is a deployed contract with no version field |
| a runner entry point (`UseTraxJobRunner`, `UseTraxRunEndpoint`, `SqsJobRunnerHandler`, `TraxLambdaFunction`) or request signing | [0006](./docs/adr/0006-a-runner-requires-an-authorization-posture.md), every runner needs a posture and runs only registered trains |
| `INonceStore`, `UseInMemoryNonceStore()`, or the `runner_nonce` table | [0009](./docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md), a signing runner shares accepted nonces through the database unless the host opts into memory; central `docs/0009` for why the table ships in Trax.Effect, and `docs/0036` for why its model does too and the store reaches it through `IDataContext` rather than SQL |
| SQL, or anything provider-shaped | [0002](./docs/adr/0002-a-database-provider-is-interchangeable.md), the difference belongs behind `ISqlDialect` |
| `OperationsService`, or anywhere that builds a work queue row | central `docs/0017`, a caller's enqueue goes through the mediator; only the allow-listed system and admin paths build their own |
| a dashboard or API action, or `OperationsService.RunTrainAsync` | central `docs/0022`, the dashboard and the GraphQL API call one operations-service method per action; neither surface carries its own copy of the logic; and central `docs/0037`, a run applies the per-record checks a queue applies (`OnQueue`, and a subject-keyed train only inside a trusted scope) |
| what `OperationsService.QueueTrainAsync` returns when the enqueue fails | [0004](./docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md), a refusal is a failed result; a database or network failure is logged and thrown, never returned with its message |
| `UpdateSchedulerConfigAsync`, `SchedulerSettings`, or how a saved setting reaches a scheduler | [0010](./docs/adr/0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md), a save names only what it sets in the row's `overrides`, every other setting keeps its code value, and every running scheduler re-reads the row |
| an `ExecuteUpdate` or `ExecuteDelete` in `OperationsService`, `ExecutionCancellation` or a `TraxScheduler` operation | [0007](./docs/adr/0007-the-operations-surface-runs-on-inmemory.md), check `SupportsSetUpdates()` and fall back to per-row on InMemory |
| the dispatch claim, `LoadQueuedJobsJunction`, or the subject lock | central `docs/0019`, one subject's queued work runs one at a time |
| `OperationsService.RequeueExecutionAsync`, the replay link a requeue sets, or carrying `replay_decisions_of` from the work queue to the run | central `docs/0041`, a requeued run replays the decisions of the run it repeats, and only that method sets the link for a caller's run; a manifest trigger does not |
| a manifest's retry in `CreateWorkQueueEntriesJunction`, a dead-letter requeue, `RetryDecisionReplay`, or `ReplayDecisionsOnRetry` | [0017](./docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md), a retry replays the failed run's decisions at most once in a row, only from a run of the manifest's train that asked itself, recorded, and was queued with the same input; otherwise, or when an operator asks afresh, it asks afresh |
| `ResolveStaleStagedEntriesJunction`, `StaleStagedEntryTimeout` or `PromoteStaleStagedEntries` | central `docs/0018`, a stranded staged entry is cancelled by default |
| a train's `Junctions()`, including the ManifestManager, JobDispatcher and JobRunner chains | central `docs/0016`, a chain is a declaration read at host startup, so it may not read the input |
| the JobRunner chain, or anything done after a scheduled train returns | [0005](./docs/adr/0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md), the manifest update stays in the junction that ran the train, on an uncancellable token |
| `RemoteRunResponse.FailureClass`, or how either executor reads it | central `docs/0020`, the worker's class is carried, and [0001](./docs/adr/0001-remote-execution-is-a-json-wire-contract.md) for its encoding on the wire |
| `MaxRetries`, `FailureCountWindow`, `FailedCount` in `LoadManifestsJunction`, the retry backoff, how `SchedulingHelpers` decides a manifest is due after a cancelled run, or when a dependent runs again after its parent succeeds | [0014](./docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md), retries count recent failures after the first run, and a cancelled run consumes its occurrence |
| `PruneOrphanedManifests`, `Manifest.Owner`, or the startup prune in `SchedulerStartupService` | [0015](./docs/adr/0015-a-startup-prune-deletes-only-its-own-applications-manifests.md), the prune deletes only manifests its own application declared, and keeps unowned ones |
| `[TraxRemote]` routing, or a scheduler build with no routed submitter | [0016](./docs/adr/0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md), a marked train with nowhere to go fails the build |
| `RemoteRunResponse.PublicMessage`, `RemoteRunException`, or what a remote failure shows a client | central `docs/0028`, the runner offers only a plain `TrainException`'s message, and every remote failure is rebuilt as a `RemoteRunException` carrying it |

Decisions binding more than one folder live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by folder. Thirty-three name `scheduler`, three of them superseded. Besides the repository-wide conventions and
`0016` to `0020`, `0022` and `0041` (routed above), `0007` (the canonical train name is the
interface FullName) is the one this folder touches most, since it is the string stored in
`work_queue.train_name` and the one a remote run puts on the wire. The wire is lenient about
it: the executing side falls back to the short type name when the FullName does not match. The index is at
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

`tests/Trax.Scheduler.Tests.Meta/` holds fifteen convention guards. Twelve are the
repository-wide conventions shared with the other folders. The thirteenth,
`SchedulerBuilderOrderDiagnosticsTests`, is this folder's form of the builder-order check that
Trax.Effect and Trax.Mediator run as `BuilderOrderDiagnosticsTests`. The fourteenth,
`WorkQueueCreationSitesTests`, also runs in Trax.Api and Trax.Dashboard with a different
allow-list in each; here it permits only the ManifestManager's enqueue, dormant dependents, and
`TraxScheduler`'s manifest trigger and dead-letter requeue (`docs/0017`). The fifteenth,
`TestProjectsAreNotPackedTests`, keeps every project under `tests/` unpackable, since the helper
library `Trax.Scheduler.Tests.ArrayLogger` once reached nuget.org. The guards this folder's own ADRs name live with the suites they
belong to rather than in `Tests.Meta`: `RemoteRunContractTests`, `HttpRunExecutorTests` and
`LambdaRunExecutorTests` for the wire contract, `ProviderConsistencyTests` and
`SqliteSchedulerBuilderTests` for the provider swap.

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

```bash
docker compose up -d          # Postgres for the integration and stress suites
dotnet test
```
