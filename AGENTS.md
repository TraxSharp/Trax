# Trax

One repository, one folder per package family. This file covers what spans folders: the layout,
how they depend on each other, how to build, test and release. **Read the `AGENTS.md` in the
folder you are working in** as well; it routes to that folder's decisions and names the guards
that enforce them.

## Where the rules live

| You want | Look in |
|---|---|
| Why a rule exists, and what it cost | `<Folder>/docs/adr/`, indexed in that folder's `docs/adr/README.md` |
| Why a rule that spans folders exists | `Trax.Docs/adr/`, indexed by folder in `Trax.Docs/adr/README.md` |
| What the rule *is* | the published docs under [traxsharp.net/docs](https://traxsharp.net/docs) (source in `Trax.Docs/`) |
| How to record a new decision | `.claude/skills/recording-decisions/` |

Published rule pages, all under [traxsharp.net/docs](https://traxsharp.net/docs):

| Topic | Page |
|---|---|
| Where each kind of thing lives | `/docs/reference/project-layout` |
| Builder pattern and state markers | `/docs/reference/builder-pattern` |
| `Add*` vs `Use*` extension naming | `/docs/reference/extension-method-naming` |
| Test folder layout, fixtures, determinism, coverage | `/docs/reference/test-conventions` |
| Writing a migration, provider dialects | `/docs/reference/writing-migrations` |
| Registration order, and what fails when it is wrong | `/docs/reference/registration-order` |
| The shipped architecture-guard packages | `/docs/reference/architecture-guards` |
| Docs conventions, voice, link format | `/docs/reference/contributing-docs` |
| Debugging across processes with `trax.log` | `/docs/effect/debugging-with-the-log-table` |

**A code change and its docs change land in the same pull request.** If you change a public API,
the page under `Trax.Docs/sdk-reference/` changes with it, and a new migration gets its section in
`Trax.Docs/migration-guides/database-migrations.md`. `PublicApiIsDocumentedTests` and
`MigrationsAreDocumentedTests` in `Trax.Docs` fail the build when either is missing. The site is
published from releases, not from `main` (`Trax.Docs/adr/0043`).

## Layout

```
Trax.Core/              Core utilities, the chain, the guard engines (Trax.Core.Testing)
Trax.Effect/            Effect system, data providers, state machine
Trax.Mediator/          Mediator pattern, train bus, train authorization
Trax.Scheduler/         Job scheduling, manifests, remote runners
Trax.Api/               GraphQL API layer
Trax.Dashboard/         Blazor Server dashboard UI
Trax.Cli/               The `trax` tool
Trax.Samples/           Sample applications and the dotnet new templates
Trax.Api.StateMachine/  @trax/state-machine, the TypeScript twin of the state-machine engine
Trax.Docs/              Documentation content, the central ADRs, the ADR guard
Trax.Website/           Next.js site for traxsharp.net, and the React operations dashboard
.github/                CI, release, Dependabot; per-folder CI settings in .github/ci/packages.json
```

Each .NET folder has its own `.slnx`, `Directory.Build.props`, `docs/adr/` and `AGENTS.md`. The
root holds `global.json`, `nuget.config` (nuget.org only) and `Directory.Packages.props`.

## Dependencies

Each folder may reference only the folders before it. Trax.Cli and Trax.Api are both downstream of
Trax.Scheduler and neither may reference the other:

```
Trax.Core → Trax.Effect → Trax.Mediator → Trax.Scheduler ─┬─→ Trax.Api → Trax.Dashboard ─┐
                                                          └─→ Trax.Cli ──────────────────┴─→ Trax.Samples
```

A Trax package is always a `ProjectReference` to its csproj, never a `PackageReference`, so every
build compiles against the same commit (`Trax.Docs/adr/0042`). `DependencyDirectionTests` and
`TraxReferencesAreProjectReferencesTests` in each folder's `Tests.Meta` enforce both. The only
Trax `PackageReference`s are in `Trax.Samples/templates/content/`, which is scaffolded outside the
repository; in the repository a `Directory.Build.targets` swaps them for project references.

Third-party versions are pinned once, in the root `Directory.Packages.props`, under Central
Package Management with transitive pinning; a `PackageReference` carries no `Version`.

## Building and testing

From a folder:

```bash
dotnet build          # must be zero warnings (CI builds with -warnaserror)
dotnet csharpier format .
dotnet test
```

Each folder's `.config/dotnet-tools.json` pins CSharpier; run `dotnet tool restore` first so the
formatter is that version.

Postgres-backed suites read their port from `TRAX_TEST_PG_PORT` and fall back to 5432. The compose
files (`Trax.Samples/docker-compose.yml` has Postgres and RabbitMQ) take `TRAX_PG_PORT` for the host
side of the mapping. Several projects may share one Docker daemon: start only what you need, and do
not stop or recreate containers you did not start.

Do not suppress warnings with `#pragma` or `<NoWarn>` unless there is no alternative. Prefer the
root cause: `= null!;` for uninitialised properties, `AssertLoaded()` instead of `!`, nullable
return types.

### Lockfiles

Every project commits a `packages.lock.json`, and CI restores with `--locked-mode`. A modified
lockfile you did not mean to change is a mistake; regenerate deliberately after a real dependency
change with `dotnet restore <Folder>/<Folder>.slnx --use-lock-file --force-evaluate`, for every
folder whose graph moved (a root `Directory.Packages.props` change can move all of them).

### A new runtime version forces itself on you

`Trax.Effect` is built against specific `Microsoft.Extensions.*` patch versions, which become the
floor for everything above it. A restore that violates it fails with `NU1109`/`NU1605` naming the
exact packages. **Bump only those.** Do not blanket-bump the whole `10.0.x` line: it sweeps in
`Microsoft.AspNetCore.Authentication.JwtBearer`, which drags `Microsoft.IdentityModel.*` across
many minor versions, changes `ConfigurationManager` JWKS-refresh throttling, and breaks Trax.Api's
Cognito key-rotation tests (401 instead of 200).

## CI

`.github/workflows/ci.yml` runs on every pull request. Its first job decides which folders the
change can affect: a folder runs when it or anything upstream of it changed, and a change outside
every folder runs everything. Each .NET folder then gets the same steps (locked restore, lock and
deprecation checks, build, pack with package validation, tests, the ADR guard), with the services
and databases its entry in `.github/ci/packages.json` names. **`CI result` is the one required
check.**

## Releases

**The commit type controls what a release contains; you control when it happens.** `feat:` cuts a
minor, `fix:`, `perf:` and `revert:` a patch; the rest (`ci:`, `chore:`, `docs:`, `refactor:`,
`test:`, `style:`) publish nothing. A pull request is squash-merged with its title as the commit,
so the title's type is what counts.

Merging to `main` publishes nothing. To release, dispatch **Release** in the Actions tab. It runs
the full CI over every folder, then semantic-release reads every commit since the last `v*` tag
and cuts **one** version for **every** package, at the highest bump those commits call for. Both
the versioning and the publish job wait on the protected `release` environment. After the
packages are on nuget.org it moves the `website` branch to the new tag, which publishes the site.

**Never write `BREAKING CHANGE:` in a commit body or footer** unless explicitly told to: it cuts a
major, which is permanent on nuget.org. Describe breaking changes in the PR description instead.

After a release, set `PackageValidationBaselineVersion` in each folder's `Directory.Build.props`
to the version just released, so package validation compares against it.

## Pull requests

Title: a conventional-commit subject (`fix: refuse …`, `feat: add …`). Body: what changed and
why, how it was tested, and anything it must merge after. Commit messages and PR descriptions
read as though a developer wrote them for other developers: direct, specific, no hedging.
