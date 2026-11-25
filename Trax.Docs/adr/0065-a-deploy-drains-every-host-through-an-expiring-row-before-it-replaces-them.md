---
authors: [Theauxm]
repos: [effect, scheduler, api, cli]
areas: [platform, data-model]
status: accepted
---

# A deploy drains every host through an expiring row in the database before it replaces them

Replacing a host fails what it is in the middle of, and a scheduler host that starts fails every run that was
InProgress before it started, on any host. So a deploy first drains: it sets one row, `trax.drain`, that every host
sharing the database honours by starting no new work, waits for what is busy to finish, and only then replaces the
hosts. The row lapses on its own, a schedule due meanwhile is an ordinary misfire, and the deploy reaches the database
from inside, through the app's own image, never through an endpoint.

## Status

**Accepted.** Decided with the user on 2026-11-16 (drain, wait up to a timeout, refuse or `--force`) and 2026-11-24
(the row expires, misfire policy, drain from inside, 10 minutes by default).

## Context

The hub is not the only thing that starts work. Workers are separate services that claim from the database with
`FOR UPDATE SKIP LOCKED`, so telling the hub to stop would leave them claiming. Hosts may be on a private network or a
tailnet the machine running `trax deploy` cannot reach. And a deploy can die halfway, which must not leave every host
idle for good.

## Decision

- **The drain is a row, and every host reads it.** The job dispatcher skips its cycle, the manifest manager queues no
  scheduled or dependent run (its reapers still run), and a local worker checks inside its claim's transaction. Work a
  caller queues is still written to the queue and waits. Postgres, SQLite and InMemory all keep the row.
- **The row expires.** It holds while its `until` is ahead of the reading host's clock. A deploy sets it to its
  timeout plus a 10 minute margin for the roll; the console and the API set a duration, 20 minutes by default and at
  most a day. A deploy ends it once every service has rolled and when it refuses, and a crashed or interrupted deploy's row
  lapses. A scheduler host that starts while it holds leaves it in place and skips its stuck-run recovery.
- **A schedule due while drained is a misfire.** Nothing is queued; when the drain ends, the manifest's misfire policy
  (or the scheduler's default) decides as it does for any late occurrence. No new setting.
- **Busy is what a replaced host would cut off.** A run that is Pending or InProgress, except a Pending run whose job
  waits in the queue unclaimed (it survives the roll) and the scheduler's own trains; and a state-machine instance not
  at rest, meaning one with an effect in flight or whose invoked run is busy. Machines already record both on the
  instance row (`effect_token`, `invoke_token`), so no machine definition marks resting states.
- **The deploy drains from inside.** It runs the new version's image once with `--trax-drain <timeout>`, as it runs
  `--trax-migrate`: a Kubernetes Job, an ECS run-task, a one-shot compose or podman container. The process sets the
  row, waits 5 seconds for any host that read it just before, then polls the busy list. Empty, it exits 0 and leaves
  the row for the roll; past the timeout it ends the drain and exits non-zero naming what is busy, unless
  `TRAX_DRAIN_FORCE=true` (`trax deploy --force`), when it exits 0 with the row kept. `--trax-resume` ends it.
- **The console and the API share one path.** Drain and Resume on the console's scheduler page and
  `operations.config.drain` / `resume` call `IOperationsService.DrainAsync` / `EndDrainAsync`, under the operations
  gate (central 0022).

## Rejected

- **Telling the hub over HTTP.** Workers would not hear it, and the hub may be unreachable from the deploying machine.
- **A flag with no expiry.** A deploy that dies leaves dispatch paused until someone notices.
- **Skipping or catching up schedules as a drain-specific rule.** The misfire policy already answers it.
- **Refusing without waiting, or waiting without a limit** (2026-11-16).
- **Marking resting states on the machine definition.** The instance row already says whether an effect or a run is in
  flight.

## Consequences

- During the deploy that brings the table in, hosts on the previous version do not read it; that first drain covers
  only the hosts already on the new version.
- The roll starts the new hub beside the old one while the drain still holds, so neither starts new work and the new
  hub's recovery fails nothing, even after a forced drain left runs going. The drain ends only after every service has
  rolled: a worker the roll has not replaced yet claims nothing it would be cut off in. A run a forced roll does cut
  off is failed by the stale-run reaper. cli/0007 records the overlap.
- A crashed deploy leaves the hosts drained until the row lapses or someone resumes, even if a hub restarts.
- A train a request runs inline, a machine timer and a machine policy still run while drained. The drain waits for the
  runs they start like any other.
- A drain compares times on each host's clock, so a host whose clock is far off honours it early or late by that much.

## Exemplars

**Enforced elsewhere:** `DrainTests` in `Trax.Scheduler/tests/Trax.Scheduler.Tests.Integration` (the dispatcher, the
manifest manager, a worker, a lapsed row, misfire after the drain, the starting hub, two hubs overlapping);
`DrainStoreTests`, `DrainCommandTests` and `TraxDrainTests` in `Trax.Effect/tests/Trax.Effect.Tests.Integration`, and
`SqliteDrainTests` and `InMemoryDrainTests` beside them; `DrainOperationsE2ETests` and `ConfigOperationsTests` in `Trax.Api/tests/Trax.Api.Tests`
(GraphQL and the operator gate); the deploy targets' tests in `Trax.Cli/tests/Trax.Cli.Tests`.

## Changelog

- **2026-11-25**: A starting scheduler host no longer ends the drain, and skips its stuck-run recovery while one
  holds. Ending it at the first new hub let workers not yet rolled claim work they were then cut off in, and a forced
  roll's new hub failed the runs the old one was still finishing.
- **2026-11-24**: Recorded.
