---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A deploy drains the hosts before it rolls them, and then a hub's next copy starts beside the old one

Every target `trax deploy` writes or runs drains the hosts before it replaces any of them and ends the drain after,
and on Kubernetes and ECS a hub's new copy starts beside the old one and takes traffic before the old one stops.
Before this a Kubernetes hub used `Recreate` so that "two hubs never run at once", which took the API down for every
deploy, while ECS already rolled hubs at 100/200 and so ran two at once without a drain. Decided by the user on
2026-11-24. The drain itself, its expiry and what counts as busy are central [0065](../../../Trax.Docs/adr/0065-a-deploy-drains-every-host-through-an-expiring-row-before-it-replaces-them.md).

## Status

**Accepted.**

## Why two hubs at once is safe after a drain

Dispatch is already safe with two hubs: a worker claims a job with `FOR UPDATE SKIP LOCKED`, the dispatcher claims
work queue entries the same way, and the manifest manager runs under an advisory lock, so neither hub takes what the
other took. The one hazard is the startup recovery (`RecoverStuckJobsOnStartup`, the SCH-1 behaviour): a starting hub
fails every InProgress run that began before it started, wherever that run is, so a new hub starting beside a busy old
one fails the old one's live runs.

A host that starts while a drain is in force skips that recovery, and the drain holds until the deploy's resume runs
after every service has rolled. So the new hub fails nothing, even after `--force` left runs going on the old one,
and no old hub or worker starts work it would be cut off in during the stop. The same order fixes ECS, whose
overlapping roll had the hazard before.

## What each target does

- **Kubernetes** (and EKS): migrate Job, then a drain Job (`--trax-drain <timeout>` on the new image), then the roll,
  then a resume Job (`--trax-resume`), after a rollback too. The drain is skipped on a first deploy. A hub gets
  `RollingUpdate` with `maxSurge: 1` and `maxUnavailable: 0`, its readiness probe on the health endpoint, a preStop
  `sleep` lifecycle action of 10 seconds (the kubelet sleeps, so the image needs no shell) so the Service stops
  routing to the old pod before it gets SIGTERM, and `terminationGracePeriodSeconds` of the drain's timeout plus that
  sleep. After a drain nothing is running and a pod exits at once; with `--force`, what is still busy gets the
  drain's timeout again to finish during the stop.
- **ECS**: the drain and resume are tasks on the migrate task definition with the command and environment overridden.
  ECS stops a container within `StopTimeout`, at most 120 seconds, which every container now sets; the drain has to
  finish before the roll, not in the stop.
- **Compose**: a `drain` one-shot after `migrate`, which every service depends on, and a `resume` one-shot once they
  have started. Compose stops every container whose image changed before it starts any, so a release starts
  `migrate` and `drain` first and waits for the drain (`compose up -d migrate drain && compose wait drain`), then
  runs `compose up -d`; trax prints the three. `TRAX_DRAIN_FORCE=true` on the release forces it.
- **Podman runtime**: systemd stops a unit before its `ExecStartPre` runs, so the drain cannot live in a unit. The
  install hint plays the migrate pod, then the drain pod, and chains `systemctl --user restart` to both exiting 0,
  then plays the resume pod. The migration comes first, as on the other targets, so the drain runs on the schema it
  was built for; the migrator unit's own migration then has nothing left to do.
  A unit's restart still stops the old pod before the new one starts; there is no overlap on one host.

Past the timeout the drain ends itself and the deploy refuses with the end of its log, which lists what is busy;
`--force` rolls anyway with the drain kept until the resume.

## Considered options

**SCH-1's host leases first**, so recovery fails only runs whose host is gone. Rejected: extra large, two releases,
and the drain was wanted for its own sake on every target.

**Split the scheduler out of the API host**, so only the API overlaps. Rejected: it changes what an app is made of to
fix a deploy order.

**Keep `Recreate` on Kubernetes.** Rejected: every deploy took the API down.

## Consequences

A stack still runs one copy of a hub in steady state (`ComposeTarget.SingleRoles`): a copy that restarts outside a
deploy runs the recovery against the others' live runs. Two copies overlap only during a deploy, after the drain.
A deploy over running services takes at least the drain's settle time longer, and a stuck run blocks it until the
timeout unless it is cancelled or the deploy is forced.

## Exemplars

- `KubernetesDrainTests` pins the hub's strategy, probe, preStop and grace, the drain and resume Jobs, the order
  migrate, drain, roll, resume, the refusal with the busy list, `--force`, and the resume after a rollback.
- `AwsDrainTests` pins the same order on ECS, the task overrides and the 120-second `StopTimeout`.
- `ComposeDrainTests` and `PodmanDrainTests` pin the compose services and the podman install order.
- [CLI](/docs/reference/cli#drain-before-a-rollout) states it for users.
- `.github/ci/deploy/cluster.sh`, in the `deploy-cluster` CI job, deploys to k3s: the drain Job holds the roll over
  a run InProgress, the hub then rolls with two pods at once while a probe through the Service never fails, the run
  is left as it was ended and the drain row is gone.
- `.github/ci/deploy/podman.sh`, in the same job, does the same under the podman runtime: the drain pod holds the
  restart until the run ends, and Postgres and RabbitMQ keep running.

Not covered: a real ECS run. `.github/ci/deploy/compose.sh` runs the compose release over a live run the same way,
but no CI job runs it yet.

## Changelog

- **2026-11-25**: Exemplars name the deploy proofs in `.github/ci/deploy/`.
- **2026-11-25**: The podman install hint migrates before it drains. It drained first, so the new image's drain ran
  against the old schema, and failed outright on the release that brings `trax.drain` in.
- **2026-11-25**: The drain holds through the whole roll. A scheduler host that starts while drained skips its
  stuck-run recovery and no longer ends the drain; the resume after every service has rolled does.
- **2026-11-25**: Compose releases in three commands. A run of the stack under podman compose (docker-compose 2.26)
  showed a plain `compose up` stops the old hub and workers before the drain starts.
- **2026-11-24**: Recorded.
