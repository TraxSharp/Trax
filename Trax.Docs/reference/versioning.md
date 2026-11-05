---
title: Versioning and stability
description: "How Trax is versioned: one version for every package, what a release may change, and what Trax runs on."
type: reference
area: general
nav_order: 3
---

# Versioning and stability

Trax releases often. This page says what a version number promises, so you can decide how closely to follow it.

## One version for every package

Every Trax package, on nuget.org and npm, is released together at one version, and every package in a release was
built and tested from the same commit ([ADR 0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md)).
This has been true since 1.61.0; before it each package had its own version. Reference every Trax package at the same
version, and upgrade them together. With central package management that is one version repeated in
`Directory.Packages.props`.

## What a version number means

Releases are cut by hand, not on every merge. The version comes from the commits since the last release:

| The release contains | Version |
|---|---|
| A new feature | Minor (1.67.0 to 1.68.0) |
| Only fixes | Patch (1.68.0 to 1.68.1) |

Trax stays on 1.x. It does not cut a major version for a change to its public API. A minor release can change or
remove a public type or member, and when it does:

- The change is deliberate. Every package's public surface is a committed file that the build compares against
  ([ADR 0010](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0010-the-public-api-surface-is-a-committed-baseline.md)),
  and every pull request validates the packages against the last release on nuget.org
  ([ADR 0033](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0033-a-package-validates-against-its-last-release.md)),
  so a binary break fails the build unless it is accepted on purpose.
- The release notes on [GitHub](https://github.com/TraxSharp/Trax/releases) say what changed.
- When your code or your database has to change, [Upgrading](/docs/migration-guides) has a guide that names the
  release it applies from.

Read the release notes before a minor upgrade. A patch upgrade should need nothing.

## The database

`UsePostgres` and `UseSqlite` apply Trax's schema migrations when the application starts, so an upgrade migrates the
`trax` schema without a separate step. [Schema migrations](/docs/reference/schema-migrations) lists every migration
that changes something you can see, and the few that need a hand are under
[Upgrading](/docs/migration-guides#database-procedures).

## What Trax runs on

| | |
|---|---|
| .NET | .NET 10 only |
| Database in production | PostgreSQL. SQLite works for a single process; the in-memory provider is for tests. There is no SQL Server or MySQL provider. |
| Browser and Node | `@traxsharp/state-machine` on npm, released at the same version as the NuGet packages |
| Licence | MIT, with no commercial edition |

## Where to look

| You want to | Read |
|---|---|
| Know which packages to install | [Packages](/docs/reference/packages) |
| Upgrade across a release that needs more than a bump | [Upgrading](/docs/migration-guides) |
| See what is planned | [Roadmap](/roadmap) |
