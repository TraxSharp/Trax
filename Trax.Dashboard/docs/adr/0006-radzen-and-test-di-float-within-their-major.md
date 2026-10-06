---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# Radzen.Blazor and the test DI container float within their major version

`Directory.Packages.props` pins every package to an exact version except two:
`Radzen.Blazor` is `11.*` and `Microsoft.Extensions.DependencyInjection` is `10.*`. The
committed `packages.lock.json` files hold the version a build actually uses, and CI restores
with `--locked-mode`, so the float acts only when someone regenerates the lockfiles. Then it
moves each package to the newest release inside its major, with no edit to the pin.

## Status

**Accepted.**

## Why this is written down

Because it is the one place this repo departs from exact pins, and the comment over the
third-party group used to say "pinned to exact versions" while two of them were not. A
reader who believes the comment cannot explain a lockfile diff that moves Radzen in a pull
request nobody asked to bump it (PR #86, a `fix:`, moved it from 11.4.1 to 11.4.3).

The float is deliberate. Builds stay reproducible, because the lockfile is the version and
not the range. Radzen is the dashboard's whole UI layer and ships patch releases often,
mostly component fixes; letting a regeneration take them within 11.x keeps the dashboard
current without a pin edit for each one. The DI container is referenced only by the
integration tests and moves with the .NET 10 runtime line the repo targets.

## Considered options

**Pin both exactly, as the other repos pin everything.** Every Radzen patch would then be a
pin edit, and central ADR 0002's reason for exact pins does not carry over. That ADR is about
cross-repo `Trax.*` packages, where two halves of one release must agree and a float pulled
regressions in with no commit to revert. Here the lockfile already gives the commit to
revert, and a third-party release inside its major is not half of anything.

**A guard rejecting any floating `PackageVersion`.** Rejected with the option above: it
would enforce the pin this decision chose not to make.

## Consequences

**A lockfile regeneration can carry a Radzen or DI upgrade.** Read the lockfile diff of any
pull request that regenerates them; a moved third-party version there is this float working,
and is the change to test, not noise.

**The published package's floor rises with each release.** The `.nuspec` lists the version
the lockfile resolved, not the `11.*` range, as the minimum `Radzen.Blazor` a consumer must
have. So a dashboard release can raise its consumers' Radzen without anyone editing the pin.

**`CentralPackageFloatingVersionsEnabled` is on for the whole file.** Central Package
Management refuses any floating version without it, and with it accepts one on any package.
A new float elsewhere would restore without complaint.

## Exemplars

**Unenforced:** the decision is to allow a float, and a rule that permits something has no
violating state a test could detect. What holds the versions still between regenerations is
the `--locked-mode` restore in `.github/workflows/pull_request.yml` and `nuget_release.yml`,
which fails any build whose resolved versions differ from the committed lockfiles.

Not covered: nothing stops a third package from floating, because the opt-in above covers
every `PackageVersion` in the file.

## Changelog

- **2026-10-01**: Recorded.
