---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A sandbox runs in a Kata microVM, proved by a startup canary

Every sandbox runs under Kata Containers, a microVM with its own guest kernel, through podman on one
host and through a RuntimeClass on a cluster. gVisor is accepted only when the launcher is
configured with it by name. Before it takes a request, the launcher starts one sandbox as a canary:
it must report a kernel other than the node's, no network interface but loopback on one host, and
no connection to any of the metadata endpoints, addresses off the host, the cluster's API server,
the launcher itself or any address the deploy adds. Otherwise the launcher does not start.

## Status

**Accepted.**

## Why this is written down

Because the runtime's name proves nothing. A RuntimeClass called `kata` can map to runc, a podman
runtime entry can point anywhere, and a cluster's network plugin may not enforce NetworkPolicy at
all. The canary checks what the sandbox actually sees, so a misconfigured runtime or network fails
the launcher's start rather than its first escape.

- **Plain rootless podman.** Rejected: a container shares the host kernel, and a kernel escape
  lands as the launcher's user.
- **gVisor everywhere.** Rejected as the default: a user-space kernel, a narrower boundary than a
  VM. Kept for clusters that offer nothing else (GKE Sandbox), chosen only by name.

Each declared limit is applied by the runtime (memory, CPU, pids) or by a sized tmpfs (disk), and
the wall-clock timeout by the launcher, which kills the sandbox and fails the run with
`TimedOut`. A request over a host-wide cap is refused, never lowered.

A podman API socket is a way out of any container that holds it, so it is mounted into neither the
hub nor a sandbox; only the launcher's process has it. On one host the launcher should run as a
dedicated user whose rootless podman only it uses, so an escape lands as that user and not as
the person whose keys are on the machine.

## Exemplars

- `RuntimeCanaryTests` pins the canary's judgement: the node's own kernel, a probe reached, an
  interface on podman, or a missing runtime each refuse the start.
- `LauncherEndpointTests` pins that the launcher does not listen until the canary passes.
- `PodmanEscapeTests.cs` is the escape suite: hostile trains that try to reach the hub, the
  launcher, the metadata endpoints and a domain, write outside their directory, fork-bomb, exceed
  memory, disk and the timeout, and leave links and too many files; and the canary refusing crun
  and a missing runtime on a real podman. It needs a podman socket, which CI and trax-test do not
  have, so it skips there and runs by hand.
- The [sandbox threat model](/docs/security/sandbox-threat-model) says what is proven and what is
  not.

Not covered: Kata itself. The host this was built on has `/dev/kvm` but no Kata, so the escape
suite ran under crun with the canary told to accept it, and the canary was shown refusing crun and
a missing runtime. Nested virtualisation in CI's runners is unverified.

## Changelog

- **2026-11-24**: Recorded.
