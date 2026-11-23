---
authors: [Theauxm]
areas: [tooling, platform]
status: accepted
---

# A podman environment is written as Quadlet units, not run, with its secrets from `.env` outside the project

A kubernetes environment with `"runtime": "podman"` runs the Kubernetes target's manifests on one host with no
cluster. `trax deploy --env` writes the pod YAML and a Quadlet `.kube` unit per pod, and starts nothing; systemd runs
them. The secret values come from the `.env` beside `trax.deploy.json`, as compose's do, into a 0600 file under
`~/.config/trax/<app>-<env>/` that the units play before their pods. Decided by the user on 2026-11-23, for the
single-host part of the Kubernetes target (`Trax.Docs/adr/0062`).

## Status

**Accepted.**

## What was decided

- **Written, not run.** Like compose, the deploy writes files and prints the commands that install and start them.
  Units under systemd come back after a reboot and restart a dead pod, which is why the user's own stacks moved to
  Quadlet on 2026-11-11.
- **The migration gates the migrator's unit.** It runs in that unit's `ExecStartPre` (play the migrate pod, `podman
  wait` it) and fails the unit unless it exits 0; every other service's unit `Requires=` the migrator's.
- **Secrets from `.env`, never beside the project.** The file trax writes holds values, so it goes under the user's
  config directory, mode 0600 in a 0700 folder. The files beside the project hold none.
- **What only a cluster reads is left out.** No Namespace (each environment gets a network of its own), no Service
  (each container answers by its own name on that network, the name a Service gives it on a cluster), Postgres as a
  Deployment on a claim (`kube play` has no StatefulSet), and `UserNS=nomap` on every unit in place of `hostUsers:
  false`, which rootless podman cannot map.

## Considered options

**trax runs `podman kube play` itself**, migrate pod first, as it applies a cluster. Rejected: nothing restarts the
app after a reboot, and podman 6.2's `kube play --wait` panics on a Job, so the CLI would have to poll the migrate pod
by hand anyway.

**The user plays the Secret**, as on a cluster where trax never writes a value. Rejected by the user in favour of
`.env`, which a compose user already keeps, so moving a stack from compose to podman needs no new step.

**A Quadlet `.kube` unit for the migration that the services order after.** Rejected: Quadlet runs a `.kube` unit as
`Type=notify`, active once its pod starts, so `After=` would not wait for the migration to finish.

## Consequences

A deploy never touches the running units: `systemctl --user restart` does. `status`, `logs` and `destroy` do not
reach a podman environment and say what does. The secret file stays on disk after a deploy, readable by the user's
own processes, as the `.env` it comes from already is.

## Exemplars

- `DeployPodmanTests` pins it: only kinds `kube play` runs and no `hostUsers` or namespace in the pod files, the
  migrator's `ExecStartPre` gate and the other units' `Requires=`, no secret value beside the project, the secret file
  at 0600 under the config folder, and a missing `.env` key refused before anything is built or written.
- [CLI](/docs/reference/cli#on-one-host-under-podman) states what differs from a cluster.

Not covered: a real `podman kube play` and systemd run; no CI job runs one yet. The unit and pod files were checked
once by hand on podman 6.2.0 (Quadlet's dry run, the Postgres pod with its Secret, the migration gate on a stand-in).

## Changelog

- **2026-11-23**: Recorded.
