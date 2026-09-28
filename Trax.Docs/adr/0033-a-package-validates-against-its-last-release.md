---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, api, dashboard]
areas: [packaging, ci]
status: accepted
---

# A package validates against its last release

Each repo that ships a library turns on .NET package validation (`EnablePackageValidation`) with
`PackageValidationBaselineVersion` set to the repo's last release on nuget.org, and its pull request
workflow runs `dotnet pack` after the build. A binary break against that release (a removed member,
a narrowed accessibility, an added generic constraint) then fails the pull request instead of
surfacing as a `TypeLoadException` in a consumer.

## Status

**Accepted.**

## Why this is written down

The committed public API baseline (0010) shows *that* the surface changed, not whether a published
assembly still loads against it. Narrowing `Train.NewMonad()` to `internal` passed that baseline
and would have made every `ServiceTrain` in the published Trax.Effect fail to load (core/0002).
Trax packages declare their Trax dependencies as minimums, so a consumer routinely runs an older
downstream package against a newer upstream one, and a binary break reaches them without any
compile error to warn them.

## Considered options

**Rely on the API baseline and review.** Rejected on the evidence above: the diff of a narrowing is
one line and reads as a tidy-up.

**Validate at release time only.** The release workflow already packs, but it packs after
semantic-release has tagged the version, so a break is found after it is cut. Packing on the pull
request moves the failure before the merge.

**Derive the baseline from the latest git tag.** Rejected in favour of a literal in
`Directory.Build.props`: the literal is visible in review and cannot silently resolve to a tag
whose package never reached nuget.org. The cost is a manual step.

## Consequences

**Bumping the baseline is part of the release procedure.** After a release, the repo's
`PackageValidationBaselineVersion` moves to the version just released (the workspace `CLAUDE.md`
lists this under "Releases are manual"). Dependabot cannot see the literal. A stale baseline stays
safe, since it only compares against an older release, but leaves changes since that release
unchecked against the newer one.

**A deliberate break needs a suppression file.** An intentional break regenerates
`CompatibilitySuppressions.xml` with `-p:ApiCompatGenerateSuppressionFile=true`, committed with the
change, and is removed when the baseline moves past it.

**Trax.Cli and Trax.Samples are exempt.** Trax.Cli ships as a .NET tool with no `lib/` for a
consumer to bind to, and Trax.Samples ships only templates.

## Exemplars

**Enforced elsewhere:** `EnablePackageValidation` and `PackageValidationBaselineVersion` in each
adopting repo's `Directory.Build.props`, with a "Pack (package validation)" step in its
`.github/workflows/pull_request.yml`. Six repos adopt it, each against the release that was current
when it did: Trax.Core (baseline 1.7.3), Trax.Effect (1.56.0, every project under `src/`, so a new
package is covered without being listed), Trax.Mediator (1.22.1, Trax.Mediator and
Trax.Mediator.Testing), Trax.Scheduler (1.33.0, its five packages by name, since
`Trax.Scheduler.Tests.ArrayLogger` ships from `tests/`), Trax.Api (1.43.2, every project under
`src/`) and Trax.Dashboard (1.15.1). Each baseline moves with its repo's releases.

Not covered: Nothing checks that a
baseline was bumped after a release, or that a new packable project falls under the condition that
enables validation. Validation compares only against one release, so a break introduced and shipped
before the baseline moved is accepted as the new normal.

## Changelog

- **2026-09-27**: Trax.Effect, Trax.Mediator, Trax.Scheduler and Trax.Api adopt it.
- **2026-09-27**: Recorded, from CORE-7 and its Dashboard counterpart.
