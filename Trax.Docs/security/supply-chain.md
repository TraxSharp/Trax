---
title: Supply Chain Security
description: "How every Trax package gets from source to nuget.org and npm: pinned dependencies, split release jobs, keyless publishing, provenance and gated releases."
type: explanation
area: security
nav_order: 4
redirect_from:
  - /docs/supply-chain-security
---

# Supply Chain Security

Trax is built and published through a defense-in-depth pipeline. No single control is load-bearing: each layer bounds what a compromise of any other layer can reach, so a poisoned dependency, a hijacked action, or a tampered runner yields little on its own. This page describes the controls that protect every Trax package from source to nuget.org and npm, as they stand today, including where they stop short.

The working assumption throughout is that any code in a CI job may be compromised and any externally-influenced input may be hostile. The controls keep untrusted code and input away from the publish credential, bound what a compromised component reaches, and keep credentials short-lived wherever the service at the other end allows it.

## Pinned, integrity-verified dependencies

Every Trax package is built from the one [TraxSharp/Trax](https://github.com/TraxSharp/Trax) repository, and its .NET folders use Central Package Management. Third-party packages are pinned in a single `Directory.Packages.props` at the repository root, and project files carry no inline versions. Trax packages reference each other with `ProjectReference`, so there are no `Trax.*` pins. The pins are exact versions, and the committed lockfiles fix each resolved version and its hash. Trax.Website is an npm project and pins through its committed `package-lock.json`.

Two npm projects ship inside packages rather than beside them. The operations dashboard's React app (`Trax.Api/src/Trax.Api.Dashboard/ClientApp`) is compiled into the `Trax.Api.Dashboard` NuGet package, so its runtime dependencies (`react`, `react-dom`, `react-router-dom`, `recharts`, `urql`, `graphql`, `graphql-ws`, `lucide-react` and the fonts) are bundled into what a host serves to its operators. The engine `@traxsharp/state-machine` is published to npm. Each pins its whole graph in its own committed `package-lock.json`, has its own Dependabot directory, and is installed, in CI and in the release job alike, only with `npm ci --ignore-scripts`. The dashboard's CI job also runs `npm audit --omit=dev --audit-level=high`, and its build refuses a bundle carrying the development mock or the demo recordings. Building the `Trax.Api.Dashboard` package compiles the app from that locked install; without it, packing fails rather than ship a package with no app.

`RestorePackagesWithLockFile` is on for every project, and each one commits a `packages.lock.json` capturing the full transitive graph with content hashes. CI restores with `dotnet restore --locked-mode`, which fails the build on any drift from the committed lockfile. Because a locked restore only checks a lockfile that exists, CI also fails when a tracked project is outside the solution (so CI would never restore it) or has no committed lockfile. The npm release tooling is locked the same way and installed with `npm ci --ignore-scripts`, so a package cannot execute code merely by being installed.

NuGetAudit runs over the whole graph, direct and transitive, with every severity (`NU1901` to `NU1904`) an error. NuGetAudit only sees advisories recorded in nuget.org's vulnerability data, not a package its owner has deprecated, so CI also fails on any resolved package deprecated as `CriticalBugs` or `Legacy`. On a pull request, GitHub's dependency review fails a change that adds a dependency with a known advisory.

| Control | Mechanism |
|---|---|
| Version pinning | Central Package Management (`Directory.Packages.props`) |
| Transitive integrity | committed `packages.lock.json` + `dotnet restore --locked-mode` |
| No unlocked project | CI fails on a project outside the solution or without a lockfile |
| Known advisories | NuGetAudit (`all`, warnings as errors) + dependency review on pull requests |
| Deprecated packages | CI fails on a package deprecated as `CriticalBugs` or `Legacy` |
| No install-time execution | `npm ci --ignore-scripts` for release tooling |
| Lockfile hygiene | CI guard rejects a lockfile containing a local-only resolved version |

Dependabot, configured once in the root `.github/dependabot.yml`, proposes updates weekly for NuGet, npm, GitHub Actions, Dockerfiles, compose files and the sample applications' frontends. It waits seven days after a version is published before proposing it, so a version that is pulled soon after release never reaches a pull request. Security updates are not delayed.

## Separated build, release, and publish

The release workflow (`.github/workflows/release.yml`) is split into jobs, because a job is the isolation unit (a fresh runner with its own secret access):

- **build / test** runs the full CI (`.github/workflows/ci.yml`) over every folder, at the commit being released: restore, compile, typecheck, test, and a trial pack of every library package and of the TypeScript engine. It holds no release or publish credential, and nothing after it runs unless all of it passed.
- **coverage** uploads the test coverage to Codecov. It holds the Codecov token, and runs no restore, build or project code.
- **release** runs only for a manual dispatch of `main`. It runs semantic-release with a token that can write tags and GitHub releases, and tags the commit build / test validated. It builds and packs nothing: the only code it runs is the release tooling itself, installed with `npm ci --ignore-scripts` from its lockfile. It does not hold a publish credential.
- **build** checks out the tag, confirms it is the commit build / test validated, and packs every package at the released version with a read-only token: `dotnet restore --locked-mode` and `dotnet pack` for the NuGet packages, and `npm ci --ignore-scripts` and `npm pack` for the TypeScript engine, `@traxsharp/state-machine`. Before packing, it installs the dashboard's app with `npm ci --ignore-scripts`, which packing `Trax.Api.Dashboard` compiles into the package. It records the SHA-256 digest of every package as a job output and uploads the packages. It holds no write token and no publish credential.
- **publish** downloads those artifacts, checks that the set of packages and every digest match what the build job recorded, then attests and publishes them unchanged: npm first, then NuGet, so no NuGet package of a version ships unless npm holds that version's engine live and it is the one this run built. It stages the engine with `npm stage publish`, using the npm bundled in an exact Node.js version, and waits up to an hour for a maintainer to approve the staged version (see *Keyless publishing*). It then pushes with `dotnet nuget push`, using the exact SDK version in `global.json`, and installs, restores, builds and tests nothing. It holds the only credentials that can publish. A re-run is safe: NuGet skips a package it already has, and npm accepts a version already live, but only when the registry's integrity (SHA-512) and shasum for that version are those of the tarball in hand. A version holding other bytes, such as one someone else published first under a scope they registered, fails the job before anything reaches NuGet.

The publish credential is therefore never present in a job that executes dependency code, and publish refuses any package that is not byte-for-byte what the build job built. Nor is the write token: the dependency code that packing runs (MSBuild targets, analyzers, source generators, the engine's bundler, the dashboard's build) runs in the build job, which can write nothing. What it can still do is shape the packages that publish attests.

The version is cut before the packages are built, because semantic-release decides it and needs push access to the repository even to compute it. build / test has already tried the same packs at the same commit, so a pack that fails there stops the release before a tag exists; a build job that fails for a passing reason, such as a network error, is re-run against the same tag without cutting another version.

## Least-privilege workflow tokens

Workflows default to `permissions: {}` and widen only what each job needs. The build job is `contents: read`. Checkout runs with `persist-credentials: false`, so a token is never left in `.git/config` for later steps to read, in every job but one: the commit job of the Dependabot lockfile heal checks out with `persist-credentials: true`. That job runs no dependency code. The lockfile patch it applies comes from a separate job that does the restore with a read-only token, and the commit job applies it restricted to lockfile paths, commits only regular lockfiles, builds the commit in a step that holds no token, and creates it through the GitHub API rather than with `git push`. Secrets are bound to the single step that uses them and referenced as quoted shell variables, never interpolated into a command line.

## Pinned actions and images

Every `uses:` reference is pinned to a full commit SHA, and every service-container image is pinned to a digest. Moving a tag does not change what a workflow executes on its next run; updates arrive as reviewable changes to the pinned SHA. The repository pins its SDK in the root `global.json` with roll-forward disabled.

## Reviewed changes

The root `.github/CODEOWNERS` file names the maintainer for every path, and lists the files that decide what CI runs, what gets restored and how a release is cut (`.github/`, the `Directory.*.props` files, `global.json`, the release tooling) explicitly. The organisation ruleset requires a code owner's approval on `main`.

On every pull request, CodeQL analyses the C# sources and the workflows (with `build-mode: none`, so the analysis runs no restore, MSBuild targets or package code), and zizmor audits the workflows for template injection, persisted credentials, unpinned actions and dangerous triggers.

## Keyless publishing

Packages are published to nuget.org with Trusted Publishing. The publish job exchanges a short-lived GitHub OIDC token for a temporary, single-use nuget.org API key (valid roughly one hour) scoped by a publishing policy to the exact repository, workflow file, and deployment environment. `@traxsharp/state-machine` reaches npm the same way: npm's trusted publisher for the package names the same repository, workflow file and environment, and `npm stage publish` exchanges the OIDC token itself. No long-lived publishing key is stored, rotated, or exposed.

The npm trusted publisher may only stage, never publish directly or move a dist-tag, so nothing the publish job holds can put a version live on its own. A staged version is not public until a maintainer approves it with two-factor authentication. The job prints the staged version's id and the tarball's shasum; the approver checks that `npm stage list @traxsharp/state-machine` shows that shasum for that id, then approves it with `npm stage approve <id>` or under the package's *Staged Packages* tab. The job waits for the version to go live, checks that the live version holds the tarball's bytes, and only then pushes to NuGet. If nobody approves within the hour, the job fails before NuGet; approving the staged version and re-running the job finishes the release.

npm lets a trusted publisher be added only to a package that already exists, so the package's first version on npm is a placeholder a maintainer published by hand, which ships no code and is deprecated once the first release is live. A newly added trusted publisher expires unless it is used within two days, so it is added shortly before the first release that uses it.

Two long-lived secrets remain, each bound to one job: the Codecov upload token, held only by the coverage job, and the website deploy key, held only by the Website workflow's job behind the protected `website-deploy` environment. That workflow runs only when dispatched by hand; no release or CI job calls it. It moves the `website` branch over SSH with the deploy key, offered only to GitHub's pinned host key.

## Signed build provenance

Every package version published since 2026-07-02, when trusted publishing and attestation were introduced, carries SLSA build provenance, generated keylessly in the publish job and recorded in the Sigstore transparency log. Versions published before that date have none. The attestation binds each package digest to the repository, workflow, and commit that produced it. The npm package also carries npm's own provenance statement, shown on its npmjs.com page and checked by `npm audit signatures`. From 1.61.0 that repository is `TraxSharp/Trax`; earlier versions were attested to the separate repository each package was built in then, such as `TraxSharp/Trax.Core`.

nuget.org adds its own repository signature to a package after it is pushed, as a `.signature.p7s` entry inside the `.nupkg`. That changes the file's digest, so `gh attestation verify` fails on the package exactly as downloaded. Deleting that one entry gives back the bytes the publish job attested. Verify a copy, not the package your restore uses:

```bash
cp trax.core.1.61.0.nupkg verify.nupkg
zip -d verify.nupkg .signature.p7s
gh attestation verify verify.nupkg --repo TraxSharp/Trax
```

## Deterministic builds

Builds are deterministic: the same source and toolchain produce byte-for-byte identical output. Module identifiers and timestamps are content-based rather than random, and CI normalizes embedded source paths. Determinism is the prerequisite for independent verification that a published binary matches its source.

## Gated releases

A merge to `main` builds and tests but releases nothing; a release is a manual run of the release workflow, and the release job refuses any ref but `main`. Both the release and publish jobs run behind the protected `release` deployment environment, which restricts which refs may deploy and can require a human reviewer before a version is cut or a package leaves the pipeline. The environment also scopes the OIDC claim that the trusted-publishing policy checks, a claim a feature branch or pull request cannot forge.

## Runtime egress blocking

Every job runs under StepSecurity Harden-Runner in `egress-policy: block` mode, with its own `allowed-endpoints` list. A job reaches the runner's own service hosts and artifact storage, which the agent always allows, and the hosts on its list: GitHub and its release downloads for every job, the .NET installer and NuGet for a job that restores, the certificate revocation lists NuGet's signature checks read, npm for one that installs packages, Codecov for coverage, Sigstore and NuGet's push host for publishing. A connection to any other host fails, so code that reads a credential in a job cannot send it to a host of its choosing. The lists were taken from the hosts each job reached while the policy was still `audit`; a job that needs a new host fails with the blocked host named in the Harden-Runner log, and the host is added to that job's list, not to all of them. Telemetry to StepSecurity, the .NET SDK's and Codecov's error reporting are not on any list.

## Untrusted input handling

Externally-influenced fields (pull request titles, branch names, commit messages) are never interpolated into a shell or script context. Workflows run pull request code only in the restricted `pull_request` context, never in a privileged context with secrets. This keeps both untrusted code and untrusted data away from anything that could turn them into execution.

## Enforced conventions

The conventions above are enforced, not just documented. Each folder's `Tests.Meta` suite checks the dependency model: a Trax package built in the repository is referenced with `ProjectReference`, never `PackageReference` (`TraxReferencesAreProjectReferencesTests`), every reference points upstream (`DependencyDirectionTests`), and every pin in the root `Directory.Packages.props` is used by some project (`NoOrphanPackageVersionTests`). `Trax.Core.Testing` also ships architecture guards for repository structure and test hygiene that consumers can run over their own code. A change that violates a checked convention fails CI rather than drifting in silently.

## Layered, not trusted

The goal is not a set of components assumed to be trustworthy, since trust is what gets exploited. It is an arrangement where compromising any one component yields little, is detectable, and is recoverable. SHA pinning bounds what a compromised action becomes; job separation keeps the publish and release credentials away from dependency code, and the digest check keeps publish from shipping anything the build job did not build; egress blocking limits where data can go; keyless, short-lived credentials bound how long a stolen one lives; provenance and determinism let the result be verified from outside the pipeline.
