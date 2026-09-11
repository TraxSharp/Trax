---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [packaging, ci]
status: accepted
---

# Cross-repo dependencies are exact-pinned and lockfiled

Every cross-repo `Trax.*` reference is pinned to an exact version in that repo's
`Directory.Packages.props` under Central Package Management, the `PackageReference`
elements carry no `Version`, and every project commits a `packages.lock.json` that CI
restores with `--locked-mode`.

Local development overrides all of it to a single `1.99.99` through a gitignored
`trax-local.props` that `pack-local.sh` writes, so working across repos needs no version
bumping and CI never sees the override.

## Status

**Accepted.** Supersedes the floating `Version="1.*"` the repos used before, which pulled
new upstream releases automatically at the cost of builds that could not be reproduced.

## Considered options

**Floating `1.*`.** What this replaced. A build was a function of when it ran, so a green
CI run proved nothing about the next one, and an upstream regression arrived without a
commit anyone could point at or revert.

**Git submodules or a single monorepo.** Either would make the version question disappear.
Rejected because the repos publish independently to nuget.org and version independently by
design (see the package version spread any consumer sees), and collapsing them would make
that impossible rather than merely inconvenient.

## Consequences

**Pulling an upstream release is a deliberate edit**: bump the pin, regenerate the
lockfiles with no `trax-local.props` present, commit. It does not happen by itself, which
is the point.

**A new `Trax.Effect` forces its .NET runtime versions on you.** Consuming it makes the
`Microsoft.Extensions.*` patch versions it was built against the floor, and a restore that
violates it fails with `NU1109` naming the exact packages. Bump only those. Raising the
whole `10.0.x` line sweeps in `Microsoft.AspNetCore.Authentication.JwtBearer`, which drags
`Microsoft.IdentityModel.*` across many minor versions and silently changes
`ConfigurationManager` JWKS-refresh throttling, breaking Cognito key-rotation auth tests.

**A modified `packages.lock.json` is usually local-dev pollution, not a real change.** Only
a `"resolved": "1.99.99"` entry is pollution; a `"[1.99.99, )"` requested range is normal
and lives on `main`, because intra-repo project references pick up `Directory.Build.props`.

## Exemplars

- [Supply Chain Security](/docs/supply-chain-security) covers the pinning and lockfile
  controls from the security side.

**Enforced elsewhere:** `CrossRepoPackageReferenceTests` (no inline `Version` on a
cross-repo reference), `DirectoryBuildPropsVersionTests` (the `1.99.99` sentinel) and
`TraxPinLockstepTests` (one publish family moves together) in each repo's `Tests.Meta`
project, plus `RepoConventionGuards` shipped from `Trax.Core.Testing` and a CI step that
rejects any lockfile containing the local version.

Not covered: nothing checks that a pin is *current*. A repo can sit on an old upstream
release indefinitely and every guard stays green.

## Changelog

- **2026-09-11**: Recorded.
