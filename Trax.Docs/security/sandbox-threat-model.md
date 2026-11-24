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

| It tries to | It is stopped by | Proven |
|---|---|---|
| Escape through a kernel bug | Kata: its own guest kernel in a microVM. gVisor only when named. The startup canary refuses a sandbox on the node's kernel. | Canary refusal under crun and with no runtime, on podman. Kata itself: **not run**. |
| Reach the hub, the database, the launcher, a metadata endpoint, a domain | One host: no network namespace but loopback. Cluster: a deny-all NetworkPolicy, no DNS resolver, no service links; the canary refuses a cluster whose plugin lets a probe through. | One host, crun: every probe blocked, no name resolved. Cluster: **not run**. |
| Use a credential | None is there. No database, broker or cloud credential in the image's host; no ServiceAccount token; only `HOME` and `TMPDIR` set; on a cluster an admission policy refuses the environment and volumes a credential webhook would add. | Spec and policy text tested; admission **not applied to a cluster**. |
| Write outside its directory | A read-only root, the input read-only, every capability dropped, no new privileges, a non-root user. | One host, crun: every write refused. |
| Fork-bomb | A pids limit (512 by default), then the timeout. | One host, crun: held, killed at the timeout, launcher unaffected. |
| Exhaust memory | The declared memory limit, no swap beyond it; the kernel kills it and the run fails `OutOfMemory`. | One host, crun: killed and reported. |
| Exhaust disk | One host: the output and scratch are tmpfs of the declared size, so a write past it fails. Cluster: the scratch is a sized emptyDir; the output's size is polled and the run killed past it, with the claim's size as the ceiling. | One host, crun: the write fails and the run fails. Cluster polling: **not run**. |
| Run forever | The launcher kills it at its timeout and fails the run `TimedOut`. | One host, crun. |
| Smuggle something out through its output | The launcher reads the output after the sandbox has stopped and refuses it whole for a link, a device, a pipe, a path outside it, a duplicate, or more files or bytes than the caps, then writes a fresh tar of what it accepted; the hub checks that again. Only files under `files/` go back. Nothing is extracted to disk. | Unit tests on hostile tars and directories; one host, crun: links and too many files refused. |
| Run a train it was not declared for | The entry point runs only a train whose class carries `[TraxSandbox]`, by its exact name. | Unit and escape tests. |
| Lie in its result | Nothing stops it: the result is the work's own report. The hub treats it as input, and a result that is not JSON, or too large, fails the run. | |

## A compromised hub

A hub that is taken over can ask for sandboxes, as it is meant to. It cannot:

| It tries to | It is stopped by | Proven |
|---|---|---|
| Choose the image, command, environment, mounts, ServiceAccount, runtime, host network, privileges or capabilities | The request has no field for any of them, and the launcher refuses a request with a member it does not know. All of it is the launcher's deploy-time configuration. | Unit tests on the reader and the route; escape suite: no container made. |
| Ask for more than the host can give | Host-wide caps on CPU, memory, disk, time, input, result and output size; a request over one is refused. | Unit tests. |
| Run more sandboxes than the host can hold | A concurrency cap; past it the launcher answers busy at once rather than queueing. | Unit test and escape suite. |
| Replay a captured request | A signed timestamp within 60 seconds and a nonce the launcher remembers. | Unit test. One launcher replica only: the nonces are in its memory. |
| Exec into a sandbox, read its logs | The launcher has no `pods/exec` or `pods/log`, and the holder on one host never runs the sandbox's code. | Role text tested. |

## What a compromised launcher means

The launcher is the boundary. On one host it holds a rootless podman socket, so it can do what that
user can; run it as a user of its own whose podman nothing else uses, so its compromise is not the
compromise of the machine's owner. On a cluster it can create Jobs in its namespace, and the
admission policies still refuse any that do not look like the launcher's.

## What has not been proven

- **Kata.** It was not installed on the machine this was built on (`/dev/kvm` is there; Kata is not,
  and installing it needs root). The escape suite ran under crun with the canary told to accept it,
  so it proves the mechanics, not the microVM. The canary was shown to refuse crun and a missing
  runtime.
- **CI.** GitHub's runners and the `runner-personal` VM have no podman socket for the suite and no
  known nested virtualisation, so CI runs the unit tests and skips the escape suite, saying so.
- **A cluster.** The Kubernetes backend, the admission policies and the NetworkPolicy have unit tests
  on their objects and their CEL as text. None has been applied to a cluster, kind included.
