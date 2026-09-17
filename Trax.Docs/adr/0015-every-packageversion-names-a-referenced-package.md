---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [packaging, testing]
status: accepted
---

# Every PackageVersion names a package a project references

Central Package Management lets a `PackageVersion` stand alone, and with
`CentralPackageTransitivePinningEnabled` on that looks like a clean way to force a version
onto a package nothing declares. Dependabot only updates a `PackageVersion` it can tie to a
`PackageReference`, so a standalone pin is invisible to it and never moves again. Every pin
must name a package some project references; a transitive is pinned by adding the reference
to the projects that pull it.

## Status

**Accepted.**

## Considered options

Keeping the standalone pins and raising them by hand lost because the failure is silent in
both directions. A frozen pin becomes a downgrade error (NU1109) the moment any direct
dependency needs a higher version, which breaks every dependency PR in the repo until
somebody edits the pin. Worse, where the pin exists to hold a security floor, freezing it
means the floor quietly stops rising while still looking deliberate.

Both had already happened. Three repos carried `SQLitePCLRaw.bundle_e_sqlite3` at 3.0.3,
pinned for the patched native library that fixes CVE-2025-6965, while Trax.Samples, which
references the package directly, had been carried to 3.0.5 by Dependabot. Separately
Trax.Api carried an undocumented pin on `Microsoft.Extensions.Caching.Memory` at 10.0.9 that
nothing referenced, which turned every EF Core bump past 10.0.12 into a failed restore.

## Consequences

Pinning a transitive now changes what a published package declares, because the reference
has to go somewhere real. Where that somewhere is a shipped library the dependency becomes
visible to consumers, which is the intended effect for a security floor and worth a thought
for anything else.

## Exemplars

**Enforced elsewhere:** `NoOrphanPackageVersionTests` in each of the eight code repos'
`Tests.Meta` projects. It reads that repo's `Directory.Packages.props` and fails on any
`PackageVersion` whose package no csproj or `Directory.Build.props` references.

Not covered: the guard proves a pin is reachable, not that its version is current or high
enough. A pin naming a referenced package can still lag, and nothing here detects a floor
that is too low.

## Changelog

- **2026-09-17**: Recorded.
