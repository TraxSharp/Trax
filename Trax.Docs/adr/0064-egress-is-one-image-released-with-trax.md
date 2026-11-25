---
authors: [Theauxm]
repos: [egress, cli, docs]
areas: [platform, packaging, ci]
status: accepted
---

# Egress is one image, released with Trax, that trax deploy names by digest

A service limited to a list of domains is held by one image, `ghcr.io/traxsharp/trax-egress`, built from
`Trax.Egress/` in this repository and released at the Trax version. `trax deploy` renders it beside every service
that names `"egress"`, on every target that runs containers, and names it by the digest the release packed into
Trax.Cli. Stacks that are not Trax apps run the same image. Decided 2026-11-16, 2026-11-17 and 2026-11-24 (user).

## Status

**Accepted.** Makes egress a manifest choice every target answers, as
[0062](./0062-kubernetes-is-a-deploy-target-beside-compose-and-aws.md) said it would be.

## Context

Each hardened stack carried its own copy of a firewall script, a seccomp profile and an egress probe (twelve
copies of the script with eight hashes on one machine on 2026-11-11), so a fix reached only the copies someone
remembered. Those scripts blocked private ranges and left the internet open; none could limit a service to named
domains, which is what an app that calls one model API and one package feed needs.

## What was decided

- **One versioned artifact in the public repository.** The image, its seccomp profile with the generator and the
  default it is generated from, and the probe that proves it, all in `Trax.Egress/`, a top-level folder of its own
  rather than a corner of Trax.Cli: it is a package with consumers outside Trax (any container stack), released
  beside the NuGet packages, not a part of the tool. Go, standard library only, so the image is one static binary
  and nftables on a pinned Alpine.
- **The allowlist is configuration.** `TRAX_EGRESS_ALLOW` (or a file) holds `name[:port]` entries, reached over
  TLS, and `tcp:host:port` entries for the deployment's own services; `TRAX_EGRESS_POSTGRES` turns a connection
  string's hosts into TCP entries so the database's address is written only where it already is. Every command
  reads the list through one parser, so the rules, the sidecar and the check cannot disagree.
- **Split in two, fail closed.** `rules` runs once with NET_ADMIN as root and exits: an nftables table whose
  output policy is drop, sending every process's DNS to the resolver and its IPv4 TCP to the proxy, except the
  egress uid's (61000). `serve` runs as that uid with no capability. If it is not running, nothing leaves.
  Compose has no init container, so `run` applies the rules, gives up root and every capability, and serves.
- **Names, not addresses.** The resolver answers A queries for listed names only (a fresh query upstream, nothing
  else of the app's carried over) and NXDOMAIN for everything else, which closes DNS tunnelling. The proxy reads the
  SNI of a TLS connection, refuses a name not on the list for that port, resolves the name itself and connects to
  a public address it resolves to, never to the address the app dialled. An allowed name on a shared CDN therefore
  reaches only that name, and a record pointing an allowed name at the LAN or at instance metadata opens nothing.
  A TCP entry passes plain TCP when the address dialled is one its name resolves to now, so the database is
  allowed by its Service, not by a range.
- **Released by digest.** The Release workflow builds the image for linux/amd64 and linux/arm64 into one OCI index,
  saved as an OCI archive, in the read-only build job, packs the index's digest into Trax.Cli, and a job of its own,
  behind the `release` environment and the only one holding `packages: write`, pushes the archive unchanged to
  ghcr.io and checks the registry holds that digest; the publish job, which ships the NuGet packages, waits for it.
  A Trax.Cli of a version names exactly the image built with it; a development build names none and refuses
  `"egress"` unless the deploy file names an image by digest.
- **On Kubernetes, a ValidatingAdmissionPolicy instead of PSA alone.** PSA baseline refuses NET_ADMIN on any
  container, init containers included, so a namespace with egress cannot be held by PSA. The policy allows
  NET_ADMIN only on an init container named `egress-rules` running this image's digest with no command and the
  argument `rules`, and refuses it (and any capability outside the baseline set) everywhere else.
- **ECS refuses `"egress"`.** Fargate allows no NET_ADMIN, so the rules cannot be applied; the Kubernetes target
  on EKS is the way to limit a service on AWS.

## Rejected

- **Keeping it in Trax.Infra on its own version.** A second release process, and Trax.Cli would name an image
  versioned apart from it.
- **Keeping the script and syncing copies with a tool.** Still copies; still drifts.
- **A NetworkPolicy, or Cilium FQDN policies, alone.** podman's `kube play` ignores NetworkPolicy, a single podman host
  is a target, and FQDN policies differ per platform.
- **Address allowlists.** A CDN serves thousands of names from one address; allowing the address allows them all.
- **An HTTP proxy sidecar** (`HTTPS_PROXY`). It limits only what honours the variable.
- **AWS Network Firewall in the foundation.** About $290 a month per environment, and AWS only.
- **A long-lived NET_ADMIN sidecar** (the old netfw pattern). The capability would stay in the pod for its
  life; here it is held for the seconds the rules take.
- **The image in .NET.** An ahead-of-time compiled host is larger and slower to start, and the program needs
  nothing .NET offers.

## Consequences

- IPv6 egress is refused outright, and UDP other than DNS. QUIC falls back to TCP.
- A TLS client that sends no SNI, or plain text to an outside host, is refused.
- A ClientHello carrying ECH (or ESNI) is refused: its outer name is the client's choice, so deciding on it would
  let an app reach any name an allowed name's CDN serves. A client that sends GREASE ECH by default is refused with
  it.
- A `tcp:` entry, and every host `TRAX_EGRESS_POSTGRES` names, is an address allowlist, the thing rejected above
  for TLS. It is meant for single-tenant or private destinations; on a shared endpoint (a managed Postgres pooler,
  a cloud API) it reaches every tenant behind the address.
- The app must not run as uid 61000.
- The image is built for amd64 only until the release builds more.

## Exemplars

**Enforced elsewhere:** the `egress` CI suite runs the Go tests (`internal/*/..._test.go`) and `test/e2e.sh`, which
runs the image in real network namespaces and proves what it allows and refuses, with a positive control on every
run. In Trax.Cli, `EgressContractTests` holds the uid, ports and commands the CLI renders to the ones the image
uses, and `DeployEgressTests` holds every target to its answer: the sidecar on compose and Kubernetes, the policy
beside it, the refusal on ECS. The `deploy-cluster` job's `.github/ci/deploy/cluster.sh` deploys a held hub to k3s
(with `hostUsers: false` and the namespace's network policies) and runs the check inside its pod, and tries the
admission policy with NET_ADMIN on an app container, a rules container of another image and a `kubectl debug`
container adding `SYS_PTRACE`.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-24**: Proved on k3s v1.36: the rules init container's NET_ADMIN and the uid match both work in a pod
  with a user namespace of its own (`hostUsers: false`).
- **2026-11-25**: The image is pushed by its own job, the only one with `packages: write`, before publish.
- **2026-11-25**: Built for linux/arm64 as well as linux/amd64; Trax.Cli names the digest of the index of both.
- **2026-11-25**: A ClientHello carrying ECH or ESNI is refused; the outer name was not a safe thing to decide on.
- **2026-11-25**: Said that a `tcp:` entry is an address allowlist, for single-tenant destinations only.
