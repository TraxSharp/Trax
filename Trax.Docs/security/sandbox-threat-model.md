---
title: Sandbox threat model
description: "What a sandboxed train is protected against, from hostile work and from a compromised hub, how each defence is enforced, and what is not yet proven."
type: explanation
area: security
nav_order: 21
---

# Sandbox threat model

A [`[TraxSandbox]`](/docs/sdk-reference/attributes/trax-sandbox) train runs work nobody vouches for:
code an agent wrote, a build of it, its tests. This page says what that work is kept from, by
what, and how sure that is. How to set it up is [Run a train in a sandbox](/docs/scheduler/run-a-train-in-a-sandbox).

## The parts and who trusts whom

- The **hub** is the Trax host the work comes from. It holds the database and the users' sessions.
  It is trusted with the input and the result, and not with the ability to shape a sandbox.
- The **launcher** is a separate process, the only one that can start a sandbox. On one host it is
  the only process with the container runtime's socket; on a cluster its ServiceAccount can only
  create, read and delete Jobs and read pods in one namespace. It is trusted.
- The **sandbox** runs the profile's image, which the deploy pins by digest, with the input from the
  hub. Its code is trusted to be the code that was built; what it is asked to do is not.
- The **image** is trusted. A compromised image is a compromised deploy: it could, for one, lie to
  the startup canary.

## Hostile work in a sandbox

"k3s, Kata" means the escape suite (`KubernetesEscapeTests`) against the launcher on k3s 1.36 with a
Kata 4.4 RuntimeClass (guest kernel 6.18 on a 6.12 node), with the rendered NetworkPolicies and
admission policies applied. "Podman, crun" means `PodmanEscapeTests` on rootless podman, which
cannot run Kata, so it proves the mechanics and not the microVM.

| It tries to | It is stopped by | Proven |
|---|---|---|
| Escape through a kernel bug | Kata: its own guest kernel in a microVM, through a RuntimeClass. gVisor only when named. The startup canary refuses a sandbox on the node's kernel. | k3s, Kata: the canary saw the guest kernel and passed, and refused a RuntimeClass named `kata-runc` that maps to runc. Podman, crun: refused crun and a missing runtime. A real kernel escape was not attempted. |
| Reach the hub, the database, the launcher, the API server, a metadata endpoint, a domain | Cluster: a deny-all NetworkPolicy, no DNS resolver, no service links, and a canary that refuses to start when any probe is reached. One host: no network namespace but loopback. | k3s, Kata: no probe reached (hub and database stand-ins by Service and pod IP, the API server, the kubelet, the launcher, IMDS, outside addresses, names). With the deny-all policy removed the canary reached the API server, the hub, the database and the internet, and refused to start. Podman, crun: every probe blocked. |
| Use a credential | None is there: no database, broker or cloud credential in the image's host, no ServiceAccount token, only `HOME` and `TMPDIR`; admission refuses the environment and volumes a credential webhook would add. | k3s: admission refused extra env, env or envFrom from a Secret, Secret and projected-token volumes, and a mounted or defaulted token. |
| Write outside its directory | A read-only root, the input read-only, every capability dropped, no new privileges, a non-root user. | k3s, Kata, and podman, crun: every write refused. |
| Fork-bomb | Podman: a pids limit (512 by default). Cluster: the kubelet's `podPidsLimit` if set, the VM's memory under Kata; then the timeout. | k3s, Kata, and podman, crun: held, killed at the timeout, the launcher and the next run unaffected. |
| Exhaust memory | The declared memory limit; the kernel kills it and the run fails. Podman reports it `OutOfMemory`; Kata sometimes does, and otherwise only shows exit 137, which the run's reason then says. | k3s, Kata: killed, reported as `OutOfMemory` or exit 137 on different runs. Podman, crun: killed and reported. |
| Exhaust disk | One host: the output and scratch are tmpfs of the declared size. Cluster: the scratch is a sized in-memory emptyDir; the output is walked every poll and the run killed past its size, and no run starts, and every running one is killed, while the runs volume's filesystem has less than `RunsMinFreePercent` free (15% by default). | k3s, Kata, and podman, crun: the run is killed or its write fails, and it fails. On a cluster neither check is a hard cap per run: see below. |
| Run forever | The launcher, or the Job's deadline, kills it at its timeout and the run fails `TimedOut`. | k3s, Kata, and podman, crun. |
| Smuggle something out through its output | The launcher reads the output after the sandbox has stopped, never follows a link or opens anything but a regular file, refuses it whole for a link, a device, a pipe, a path outside it, a duplicate, or more files or bytes than the caps, and writes a fresh tar the hub checks again. Only files under `files/` go back. | Unit tests on hostile tars and directories; k3s, Kata, and podman, crun: a link and too many files refused. |
| Run a train it was not declared for | The entry point runs only a train whose class carries `[TraxSandbox]`, by its exact name. | Unit tests; k3s, Kata, and podman, crun. |
| Lie in its result | Nothing stops it: the result is the work's own report. The hub treats it as input, and a result that is not JSON, or too large, fails the run. | |

## A compromised hub

A hub that is taken over can ask for sandboxes, as it is meant to. It cannot:

| It tries to | It is stopped by | Proven |
|---|---|---|
| Choose the image, command, environment, mounts, ServiceAccount, runtime, host network, privileges or capabilities | The request has no field for any of them, and the launcher refuses a request with a member it does not know. All of it is the launcher's deploy-time configuration. On a cluster, admission refuses any Job or pod in the namespace that is not the launcher's shape, whoever creates it. | Unit tests; k3s and podman: no Job or container made. k3s admission refused, by server-side dry run, no or another RuntimeClass, a foreign digest, the right image by tag, two containers, another claim, the launcher's account, a launcher-labelled pod with another image, and a Job whose pods carry the launcher's label; it admitted the sandbox's own pod and Job. |
| Ask for more than the host can give | Host-wide caps on CPU, memory, disk, time, input, result and output size; a request over one is refused. | Unit tests. |
| Run more sandboxes than the host can hold | A concurrency cap; past it the launcher answers busy at once rather than queueing. | Unit test; k3s, Kata (cap 2), and podman, crun (cap 1). |
| Replay a captured request | A signed timestamp within 60 seconds and a nonce the launcher remembers. | Unit test. One launcher replica only: the nonces are in its memory. |
| Exec into a sandbox, read its logs, reach another namespace | The launcher's Role: Jobs, and pods read-only, in its namespace, with no subresource. | k3s: `kubectl auth can-i` as the launcher refused pods/exec, pods/log, pods/attach, pods/portforward, jobs/status, secrets, deployments, Jobs in another namespace. |
| Reach the launcher from elsewhere | The launcher's NetworkPolicy admits only the hub's namespace. | k3s: reached from the hub's namespace, blocked from another. |

## What a compromised launcher means

The launcher is the boundary. On one host it holds a rootless podman socket, so it can do what that
user can; run it as a user of its own whose podman nothing else uses, so its compromise is not the
compromise of the machine's owner. On a cluster it can create Jobs in its namespace, and the
admission policies still refuse any that do not look like the launcher's.

## What has not been proven

- **A kernel escape.** The microVM boundary is what Kata provides; the suite shows a sandbox runs on
  a guest kernel, not that the guest cannot break out.
- **Kata on one host with podman.** It cannot be done: Kata 4 has no OCI command line for podman to
  call. One machine with Kata is k3s and the Kubernetes backend, which is what was proven. The podman
  backend ran under crun with the canary told to accept it, so it proves the mechanics only; its
  isolating runtime would be gVisor, which was not run.
- **Managed clusters.** Only k3s was used. AKS, EKS with Kata nodes and GKE with gVisor were not.
- **A hard disk cap per run on a cluster.** The output is walked every poll, so a sandbox can
  overshoot by what it writes between polls, and a walk does not see a file it deleted while still
  holding it open. The free-space floor reads the filesystem's own count, which does see such a
  file, but it is shared: one hostile run can use the volume down to the floor, and the floor then
  kills every run on it. The claim's size is a ceiling only where its storage class enforces
  capacity; k3s's default `local-path` does not, so there the volume is the node's disk and the
  floor is all that keeps the node's disk from filling.
- **Isolation in CI.** GitHub's runners have no cluster and no known nested virtualisation. CI runs
  `PodmanEscapeTests` on the runner's rootless podman under crun, with the canary told to accept it,
  so it proves the mechanics on every change and not the isolation; `KubernetesEscapeTests` skips
  and runs by hand. The `runner-personal` VM has no podman socket, so both skip there.
