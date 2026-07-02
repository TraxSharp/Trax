---
layout: default
title: Supply Chain Security
nav_order: 11
section: Guides
---

# Supply Chain Security

Trax is built and published through a defense-in-depth pipeline. No single control is load-bearing: each layer bounds what a compromise of any other layer can reach, so a poisoned dependency, a hijacked action, or a tampered runner yields little on its own. This page describes the controls that protect every Trax package from source to nuget.org.

The working assumption throughout is that any code in a CI job may be compromised and any externally-influenced input may be hostile. The controls keep untrusted code and input away from credentials, bound what a compromised component reaches, stop stolen data from leaving, and keep every credential short-lived.

## Pinned, integrity-verified dependencies

Every repository uses Central Package Management. Cross-repo `Trax.*` references and third-party packages are pinned to exact versions in a single `Directory.Packages.props` per repo, and individual project files carry no inline versions. Each project commits a `packages.lock.json` capturing the full transitive graph with content hashes.

CI restores with `dotnet restore --locked-mode`, which fails the build on any drift from the committed lockfile, and with the npm release tooling locked the same way (`npm ci --ignore-scripts`). Install scripts are disabled, so a package cannot execute code merely by being restored. A poisoned or substituted transitive dependency cannot enter a build unnoticed, and builds resolve the same bytes every time.

| Control | Mechanism |
|---|---|
| Exact version pinning | Central Package Management (`Directory.Packages.props`) |
| Transitive integrity | committed `packages.lock.json` + `dotnet restore --locked-mode` |
| No install-time execution | `npm ci --ignore-scripts` for release tooling |
| Lockfile hygiene | CI guard rejects a lockfile containing a local-only resolved version |

## Isolated build, release, and publish

Each release pipeline is split into three jobs along trust boundaries, because a job is the isolation unit (a fresh runner with its own secret access):

- **build / test** runs all dependency code (restore, compile, tests) and holds no release or publish credential.
- **release** runs the release tooling and holds the GitHub API credential, not the publish credential.
- **publish** consumes the built artifact and pushes it. It runs no installs or test code and holds only the credential that can publish.

The publish credential is therefore never present in any job that executes third-party code.

## Least-privilege workflow tokens

Workflows default to `permissions: {}` and widen only what each job proves it needs. The build job is `contents: read`. Checkout runs with `persist-credentials: false` wherever the job does not itself push, so a token is never left in `.git/config` for later steps to read. Secrets are bound to the single step that uses them and referenced as quoted shell variables, never interpolated into a command line.

## Pinned actions and images

Every `uses:` reference is pinned to a full commit SHA, and every service-container image is pinned to a digest. A maintainer (or an attacker who moves a tag) cannot silently change what a workflow executes on its next run. Updates arrive as reviewable changes to the pinned SHA.

## Keyless publishing

Packages are published to nuget.org with Trusted Publishing. The publish job exchanges a short-lived GitHub OIDC token for a temporary, single-use nuget.org API key (valid roughly one hour) scoped by a publishing policy to the exact repository, workflow file, and deployment environment. No long-lived publishing key is stored, rotated, or exposed.

## Signed build provenance

Every published package carries SLSA build provenance, generated keylessly in the publish job and recorded in the Sigstore transparency log. The attestation binds each package digest to the repository, workflow, and commit that produced it. Consumers can verify it:

```bash
gh attestation verify <package>.nupkg --repo TraxSharp/<repo>
```

## Deterministic builds

Builds are deterministic: the same source and toolchain produce byte-for-byte identical output. Module identifiers and timestamps are content-based rather than random, and CI normalizes embedded source paths. Determinism is the prerequisite for independent verification that a published binary matches its source.

## Gated releases

The publish job runs behind a protected deployment environment that restricts which refs may deploy and can require a human reviewer before any package leaves the pipeline. The environment also scopes the OIDC claim that the trusted-publishing policy checks, a claim a feature branch or pull request cannot forge.

## Runtime egress control

Jobs run under a runtime egress monitor (StepSecurity Harden-Runner). It establishes a per-job network baseline and, once tuned, restricts outbound connections to an allowlist, so even code that reads a credential cannot send it to an arbitrary host.

## Untrusted input handling

Externally-influenced fields (pull request titles, branch names, commit messages) are never interpolated into a shell or script context. Workflows run pull request code only in the restricted `pull_request` context, never in a privileged context with secrets. This keeps both untrusted code and untrusted data away from anything that could turn them into execution.

## Enforced conventions

The conventions above are enforced, not just documented. `Trax.Core.Testing` ships architecture guards that the test suite of every repo runs against its own tree: the dependency model (centrally-managed versions, no inline versions on cross-repo references), repository structure, and test hygiene. A change that violates a convention fails CI rather than drifting in silently.

## Layered, not trusted

The goal is not a set of components assumed to be trustworthy, since trust is what gets exploited. It is an arrangement where compromising any one component yields little, is detectable, and is recoverable. SHA pinning bounds what a compromised action becomes; job separation bounds what compromised code reaches; egress control bounds where stolen data goes; keyless, short-lived credentials bound how long a stolen one lives; provenance and determinism let the result be verified from outside the pipeline.
