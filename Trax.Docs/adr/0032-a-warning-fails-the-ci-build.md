---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [ci]
status: accepted
---

# A warning fails the CI build

Every .NET repo builds with zero warnings, and until now only people held that line. CI passes
`-warnaserror` to the `dotnet build` step of both the pull request and the release workflow, so
a warning fails the build that would have merged or shipped it. Local builds are left alone:
the flag lives in the workflow, not in `Directory.Build.props`.

## Status

**Accepted.**

## Why this is written down

The rule was already stated, and broken in three repos at once. Seven Radzen nullability
warnings reached Trax.Dashboard `main` with CI green. Trax.Samples built its JobHunt projects
with `MSB3277`, an EF Core Relational version conflict resolved at 10.0.4 in some projects and
10.0.12 in others. Trax.Core showed the cost best: a Dependabot bump moved the analyzer's
`Microsoft.CodeAnalysis.CSharp` to 5.9.0 while `global.json` pins SDK 10.0.301, whose compiler
is 5.6. Every project then warned `CS9057` and silently ran without the analyzer, and that
build shipped in Trax.Core 1.7.3. Each was visible in the build log of a green run.

## Considered options

**`TreatWarningsAsErrors` in each `Directory.Build.props`.** Rejected because it also fails the
local edit loop, where an unused variable halfway through a change is not worth stopping for,
and because `pack-local.sh` builds every repo in turn: one warning in a freshly packed upstream
would stop the whole chain instead of surfacing at review.

**A guard test that parses build output.** Rejected as a second mechanism for what the compiler
already reports. A test also runs after the build it would be judging.

**Leave it to review.** This is what failed above.

## Consequences

Restore warnings are covered too. `dotnet build --no-restore` replays the warnings NuGet wrote
to `project.assets.json`, so a redundant reference (`NU1510`) or an unresolved version conflict
fails the build step even though restore ran earlier. That was checked, not assumed.

A Dependabot PR that introduces a warning now fails instead of merging. For Roslyn this is the
point: an analyzer loads into the compiler of the SDK that `global.json` names, so its
`Microsoft.CodeAnalysis.CSharp` must not pass that compiler's version. Trax.Core holds its
analyzer on an older Roslyn with a `VersionOverride` for exactly this reason.

A warning raised by a pinned upstream package cannot be fixed in the downstream repo. Fix it
upstream and release, and bump the pin. A project-scoped `<NoWarn>` with a comment naming the
upstream cause is the last resort, never a repo-wide one.

## Exemplars

**Enforced elsewhere:** the `Build` step in `.github/workflows/pull_request.yml` and
`.github/workflows/nuget_release.yml` of Trax.Core, Trax.Effect, Trax.Mediator,
Trax.Scheduler, Trax.Api, Trax.Dashboard, Trax.Cli and Trax.Samples, and in Trax.Docs's
`pull_request.yml`, each running `dotnet build --configuration Release --no-restore -warnaserror`.

Not covered: only what the build step compiles. A project outside the solution CI builds, such
as the Trax.Samples StateMachine sample while it sits outside `Trax.Samples.slnx`, and the
`dotnet new` template content are not built, so their warnings go unseen. Nothing checks that a
workflow keeps the flag.

## Changelog

- **2026-09-27**: Recorded.
