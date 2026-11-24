---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# A sandbox runs in a Kata microVM, proved by a startup canary

Every sandbox runs under Kata Containers, a microVM with its own guest kernel, through a
RuntimeClass. One machine with Kata is k3s and the launcher's Kubernetes backend: Kata 4 has no OCI
command line, so podman cannot run it, and the podman backend is only ever as isolating as gVisor.
gVisor is accepted only when the launcher is configured with it by name. Before it takes a request, the launcher starts one sandbox as a canary:
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

- `KubernetesEscapeTests.cs` is the same suite against a launcher on a cluster. Run on k3s 1.36
  with Kata 4.4 (nested KVM): the canary passed under the `kata` RuntimeClass (guest kernel 6.18,
  node 6.12) and refused one named `kata-runc` that maps to runc, and refused when the deny-all
  NetworkPolicy was removed (the sandbox then reached the API server, the hub and the internet).

Not covered: CI. Its runners have no podman socket, no cluster and no known nested
virtualisation, so both suites skip there and run by hand.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-24**: Podman cannot run Kata 4, so one machine with Kata is k3s with the Kubernetes
  backend, and the podman backend says so when told to use Kata. Proved on k3s with Kata.
