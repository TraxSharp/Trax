---
name: trax-upgrade
description: Use when moving an app built on Trax .NET from one Trax version to another, bumping or updating Trax packages (Trax.Core, Trax.Effect, Trax.Mediator, Trax.Scheduler, Trax.Api, Trax.StateMachine, Trax.Cli, @traxsharp/state-machine), or fixing a restore, build or startup failure that followed such a bump. Finds the current and target versions, lists every Trax migration guide between them, applies each, handles the NU1109 and NU1605 package floors and the schema migrations applied at startup, then restores, builds, tests and reports what could not be applied.
metadata:
  internal: true
---

# Upgrade Trax

Use this when the task is a Trax version bump, or a failure that started with one. Load `trax` first if you have
not. Most upgrades are a package bump: every Trax package releases at one version, and `UsePostgres` and
`UseSqlite` apply the schema migrations when the host starts. The [migration guides](https://traxsharp.net/docs/migration-guides)
cover the releases that need more.

## Decide first

- **The current version.** Read every `Trax.*` entry in `Directory.Packages.props` (`PackageVersion`), or in each
  `.csproj` (`PackageReference Version=`) if the app does not use central package management. Since Trax 1.61.0
  they are all one version. If they differ, the app is either older than 1.61.0, when each package had its own
  version and the older guides name the package, or it has drifted; compare each package with the guides one by one.
  An app on `Trax.Effect.StateMachine*` is on 1.67.0 or older.
- **The target version.** The one the user names; otherwise the latest release (`dotnet list package --outdated`
  lists it). Never a prerelease unless asked. Release notes for each version since 1.61.0 are on the `v<version>`
  releases at [TraxSharp/Trax](https://github.com/TraxSharp/Trax/releases).
- **Everything that carries a Trax version.** All of these move together:
  - `Directory.Packages.props`, and any `.csproj` with a Trax `Version=`. A trains library made by `trax generate`
    pins its own versions in its csproj, because the hub's `Directory.Packages.props` does not reach it.
  - The `trax` tool, `Trax.Cli`, in `.config/dotnet-tools.json` or `./dotnet-tools.json`.
  - `@traxsharp/state-machine` in a frontend's `package.json`. It releases at the same version as the NuGet packages.

## Steps

1. **List the guides that apply.** A guide applies when its release is newer than the current version and no newer
   than the target. Read each one whole before editing, oldest first: some say to read another first.
2. **Bump every Trax version in one edit** to the target. A renamed package (see the table) changes its id too.
   `dotnet tool update Trax.Cli --version <target>` for the tool, `npm install @traxsharp/state-machine@<target>` for
   the frontend.
3. **Restore.** On `NU1605` or `NU1109`, raise only the packages the error names, to the floors on
   [Packages](https://traxsharp.net/docs/reference/packages#requirements). If the app commits `packages.lock.json`,
   regenerate it with `dotnet restore --force-evaluate`.
4. **Apply each guide**, oldest first. Several changes compile unchanged and only show at runtime, so a green build
   does not mean a guide is done.
5. **Build with warnings as errors**, then fix what is left from the guides, not by guessing at the new API.
6. **Regenerate what the CLI wrote**: machine twins (`trax machine generate`, then `trax machine check`) and the
   frontend client (`trax client ... --check`), with the arguments the repository already uses (its CI or
   `package.json` scripts). A twin or client from the old version is out of date after a machine wire change.
7. **Test.** Name each test project (`dotnet test tests/<Name>.Tests`), then start the host once against a copy of
   a real database, not production, so the schema migrations run. If it refuses to start, read the message: it
   names the cause and usually the page.
8. **Report** what you changed per guide, what you could not apply and why, and every step that needs a person:
   a database procedure, a deploy order, clients that must ship with the hosts.

## Every migration guide

Newest first. "Applies from" is the release that needs it.

| Guide | Applies from |
|---|---|
| [Move to the Trax.StateMachine packages](https://traxsharp.net/docs/migration-guides/trax-statemachine-packages) | Trax 1.68.0 (the guide says "the first release after 1.67.0") |
| [Reach trains through your own documents](https://traxsharp.net/docs/migration-guides/reach-trains-through-your-own-documents) | Trax 1.67.0 |
| [Adapt to the machine wire changes](https://traxsharp.net/docs/migration-guides/machine-wire-changes) | Trax 1.64.0 |
| [Regenerate twins after the trax machine generate defaults changed](https://traxsharp.net/docs/migration-guides/trax-machine-generate-defaults) | Trax 1.63.1 |
| [Move off the generic state-machine mutations](https://traxsharp.net/docs/migration-guides/generic-state-machine-mutations) | Trax 1.63.1 |
| [Upgrade schedulers from Trax.Effect 1.57.2 or earlier](https://traxsharp.net/docs/migration-guides/scheduler-leader-lock) | Trax.Effect 1.57.3 |
| [Migrate to qualified principal ids](https://traxsharp.net/docs/migration-guides/qualified-principal-ids) | Trax.Api 1.44.0 |
| [Adapt to the enqueue and outcome changes](https://traxsharp.net/docs/migration-guides/enqueue-and-outcome-changes) | Trax.Core 1.7.2, Trax.Effect 1.54.2, Trax.Mediator 1.22.1, Trax.Scheduler 1.32.2, Trax.Api 1.43.2 |
| [Move trains off RunInternal and Activate](https://traxsharp.net/docs/migration-guides/runinternal-and-activate) | Trax.Core 1.7.2, Trax.Effect 1.54.2 |
| [Upgrade to HotChocolate 16](https://traxsharp.net/docs/migration-guides/hotchocolate-16) | Trax.Api 1.41.0; its attribute refusal from Trax.Api 1.43.0 |
| [Migrate from Hangfire](https://traxsharp.net/docs/migration-guides/migrate-from-hangfire) | Trax.Scheduler 1.0.0; only for builds before it that called `UseHangfire` |

Database procedures. These apply on a condition, not at a version:

| Procedure | When |
|---|---|
| [Repair a partial migration 014](https://traxsharp.net/docs/migration-guides/repair-partial-migration-014) | Postgres migration 014 (in Trax.Effect from 1.0.0) stopped partway and the next start fails on `CREATE TABLE trax.manifest_group` |
| [Install pg_trgm before migration 063](https://traxsharp.net/docs/migration-guides/install-pg-trgm) | Crossing Trax.Effect 1.60.0 on Postgres, when the role Trax migrates with cannot create the `pg_trgm` extension |
| [Turn off state-machine change notices](https://traxsharp.net/docs/migration-guides/turn-off-snapshot-change-notices) | Optional, from Trax 1.63.0 (migration 080) on Postgres, when instance writes contend on the notify lock |

## Schema migrations

`UsePostgres` and `UseSqlite` apply every pending migration when they are called during service registration, so the
first host on the new version migrates the database for every host on it. Postgres scripts run without a
transaction, so one that stops partway keeps what it ran and runs again from the top at the next start; one that
cannot take its table lock in five seconds is retried, then startup fails with `55P03`. A host that calls `SkipMigrations()` must run them itself before the new version serves
traffic. What each migration changes that a host can see is on
[Schema migrations](https://traxsharp.net/docs/reference/schema-migrations); read the sections between the two
versions, and take a backup first.

## Known mistakes

- **Bumping only the package with the feature you want.** Every `Trax.*` package goes to the target together,
  including the tool, the npm engine and a generated trains library.
- **Bumping the whole `10.0.x` line** because restore failed on `Microsoft.Extensions.*`. Raise only the packages
  the `NU1109`/`NU1605` error names. A blanket bump pulls `Microsoft.AspNetCore.Authentication.JwtBearer` and
  `Microsoft.IdentityModel.*` forward and changes JWKS refresh behavior.
- **Stopping at a green build.** The enqueue, principal-id and machine-wire changes alter runtime behavior and
  stored data; follow each guide to its end.
- **A rolling deploy across Trax.Effect 1.57.3** on a Postgres scheduler fleet. Old and new hosts take different
  advisory locks and both lead; stop every old host first.
- **Leaving `Trax.Effect.StateMachine*` beside `Trax.StateMachine*`.** Swap the ids and namespaces; the old
  packages are deprecated and get no releases.
- **Getting past a refusal by turning a check off.** If the host now refuses to start (an exposed train without
  `[TraxAuthorize]`, HotChocolate's `[Authorize]` where Trax's attribute belongs), apply the guide;
  never remove the gate, disable masking or skip migrations to make it start.
- **Deploying hosts without their clients** after a machine wire change. Regenerate the twins and clients and ship
  them with the hosts.
