# Decisions

Why a thing in `Trax.Scheduler` is the way it is, which alternatives were weighed, and what
each cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is
a deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Scheduler` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`scheduler/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job pulls the guard as an action,
so CI needs no other repo checked out. Running it locally does, because the command below
builds the guard from the Trax.Docs checkout beside this one:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas scheduling,providers,platform,testing \
  --census-root tests/Trax.Scheduler.Tests.Meta
```

Without `--census-root` the census is never added to the run, so nothing named
`census/classified` is printed and the local command is weaker than the job above.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `platform` | [0001](./0001-remote-execution-is-a-json-wire-contract.md), [0004](./0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md), [0006](./0006-a-runner-requires-an-authorization-posture.md), [0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md), [0016](./0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md) |
| `providers` | [0002](./0002-a-database-provider-is-interchangeable.md), [0007](./0007-the-operations-surface-runs-on-inmemory.md), [0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md) |
| `scheduling` | [0001](./0001-remote-execution-is-a-json-wire-contract.md), [0002](./0002-a-database-provider-is-interchangeable.md), [0003](./0003-a-runtime-retention-override-replaces-only-the-default.md), [0004](./0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md), [0005](./0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md), [0006](./0006-a-runner-requires-an-authorization-posture.md), [0007](./0007-the-operations-surface-runs-on-inmemory.md), [0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md), [0010](./0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md), [0011](./0011-a-re-seed-writes-only-the-settings-the-code-states.md), [0014](./0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md), [0015](./0015-a-startup-prune-deletes-only-its-own-applications-manifests.md), [0016](./0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md), [0017](./0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-remote-execution-is-a-json-wire-contract.md) | Remote execution crosses the process boundary as JSON, not as a compiled type | scheduling, platform |
| [0002](./0002-a-database-provider-is-interchangeable.md) | Swapping the database provider changes no scheduler registration | scheduling, providers |
| [0003](./0003-a-runtime-retention-override-replaces-only-the-default.md) | A runtime retention override replaces only the default, never a per-train retention | scheduling |
| [0004](./0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md) | An enqueue refusal is a result; an infrastructure failure is thrown | scheduling, platform |
| [0005](./0005-a-scheduled-runs-bookkeeping-lives-in-the-junction-that-ran-it.md) | A scheduled run's bookkeeping lives in the junction that ran it | scheduling |
| [0006](./0006-a-runner-requires-an-authorization-posture.md) | A runner requires an authorization posture and runs only registered trains | scheduling, platform |
| [0007](./0007-the-operations-surface-runs-on-inmemory.md) | The operations surface runs on the InMemory provider | scheduling, providers |
| [0009](./0009-a-runner-shares-its-accepted-nonces-through-the-database.md) | A runner shares its accepted nonces through the database | scheduling, platform, providers |
| [0010](./0010-a-settings-save-writes-only-what-it-names-and-every-scheduler-applies-it.md) | A settings save writes only what it names, and every running scheduler applies it | scheduling |
| [0011](./0011-a-re-seed-writes-only-the-settings-the-code-states.md) | A re-seed writes only the settings the code states | scheduling |
| [0014](./0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md) | A manifest's retries count recent failures, and a cancelled run consumes its occurrence | scheduling |
| [0015](./0015-a-startup-prune-deletes-only-its-own-applications-manifests.md) | A startup prune deletes only its own application's manifests | scheduling |
| [0016](./0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md) | A `[TraxRemote]` train with nowhere to go fails the build | scheduling, platform |
| [0017](./0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md) | A manifest's retry replays the decisions of the run it retries, once | scheduling |
