---
authors: [mark-keaton]
repos: [core]
areas: [ci]
status: proposed
---

# Analyzer rules are a vendor's default set, every one an error

A list of analyzer rules picked one at a time is a list somebody has to defend one at a time,
and it drifts with whoever edited it last. The profile is instead taken whole from
Meziantou.Analyzer: every rule the package reports in a build is an error, in `src` and tests
alike, and nothing is chosen. The root `.globalconfig` is generated from the package's own
default configuration and is never edited by hand, and no project, file or line opts out.

## Status

**Proposed.** A prototype over Trax.Core. The other folders join by referencing the package
from their `Directory.Build.props` and clearing what it reports.

## Considered options

Every rule the package ships lost because the package ships rules that contradict each other
(return the task, await the task; one-line summaries, multi-line summaries). The rules it
leaves off are the ones its author treats as a matter of taste, so the default set is the
largest one that can be satisfied.

A curated list lost because it is the thing this replaces. Rules the package ships as silent
stay silent: it offers them in the editor and does not report them, so promoting them would be
a choice of ours, not the vendor's.

A relaxed tier for tests lost for the same reason. The cost is visible: an example quoted in
the docs carries `ConfigureAwait(false)` because the build requires it.

## Consequences

A rule is exempted only where satisfying it would change what a published package promises:
a public signature, or a documented exception. Each exemption is a line in
`.github/scripts/generate-analyzer-profile.sh` with its reason, and is copied into the
generated file as a comment.

Raising the package version can fail the build, because a new version can enable new rules.
That pull request regenerates the profile and fixes what it reports.

## The team's layer

What the vendor's set does not say is added in one place and kept short: the rules the
generator lists as added, and the APIs and syntax named in `BannedSymbols.txt` and
`BannedSyntaxes.txt` at the repository root. It bans reading the clock, chance and the console
from library code, and the null-forgiving operator in every form. This is the only part of the
profile that is a choice of ours, so it is the only part worth arguing about.

## Exemplars

**Enforced elsewhere:** `AnalyzerProfileTests` in Trax.Core's Tests.Meta project holds the
generated profile to the package's defaults and fails on a suppression, `BanLayerProbeTests`
beside it runs the analyzers over code that breaks each ban and requires the diagnostic, and
the build itself fails on any rule the profile reports.

Not covered: the guard reads the profile and the sources of the folder it runs in. A folder
that does not reference the package is not held to anything.

## Changelog

- **2026-11-17**: Added the team's layer: banned APIs, banned syntax, and no null-forgiving operator.
- **2026-11-17**: Recorded.
