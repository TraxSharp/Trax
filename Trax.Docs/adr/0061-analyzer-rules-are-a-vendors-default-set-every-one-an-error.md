---
authors: [mark-keaton, Theauxm]
repos: [core, effect, statemachine, mediator, scheduler, api, cli, samples, docs]
areas: [ci]
status: accepted
---

# Analyzer rules are a vendor's default set, every one an error

A list of analyzer rules picked one at a time is a list somebody has to defend one at a time,
and it drifts with whoever edited it last. The profile is instead taken whole from
Meziantou.Analyzer: every rule upstream reports by default is an error, in `src` and tests
alike, plus a short list of rules Trax adds on purpose. Nothing else is chosen.

Trax does not reference Meziantou.Analyzer. It keeps a copy (MIT) in `Trax.Analyzers/`, renamed
into Trax's namespace and ids (`MA0004` is `TRX0004`), and ships it as the `Trax.Analyzers`
package. The copy is refreshed from an upstream tag by `Trax.Analyzers/tools/sync-upstream.sh`.

## Status

**Accepted.** Every .NET folder, every sample, the `dotnet new` templates (with `--strict`) and
the book's Shopfront build under the profile.

## The profile

`Trax.Analyzers/rules.txt` is the profile: one line per rule, with the severity it gets in source
projects and in test projects. A rule upstream reports by default is `error` in both. A rule
upstream ships silent or off stays that way unless its line says `added by Trax`, such as TRX0045
(no blocking call, even where the caller must become async). A rule upstream has deprecated or
that contradicts another (TRX0032 asks for a token where none exists) is `none`, with the reason
on its line. `tools/generate-profile.sh` writes the package's configuration from it, per
`TraxAnalysisLevel`, so a release that adds errors does so under a new level.

On top of the rules come the bans, as files the package ships under `bans/`: reading the clock,
chance and the console, `Environment.Exit` and `FailFast`, and the null-forgiving operator in
every form. The clock is read from an injected `TimeProvider`; only a composition root names
`TimeProvider.System`.

For a consumer the package is opt-in: nothing applies until the project references it, and each
part can be turned off (an MSBuild switch per ban group, `TraxAnalyzersTests` for test projects,
a severity in `.editorconfig` for one rule). The repository uses none of those freedoms.

## How a rule is turned off here

In two places, each with its reason beside it:

- **The root `.globalconfig`**, for the whole repository. Each line says `# Exempt:` (keeping the
  rule would break a published promise: a public signature or a documented exception) or
  `# Deferred:` (the work is owed), and why.
- **A folder's `Directory.Build.props`**, for one ban group, with the reason in a comment above.
  Trax.Cli turns the console ban off: a command-line tool writes to the console.

No `#pragma`, no `[SuppressMessage]`, no `NoWarn` or `WarningsNotAsErrors` naming a rule, no
`RunAnalyzers=false`, no nearer `.editorconfig`, `.globalconfig` or ban file.

## Considered options

Referencing Meziantou.Analyzer lost because the profile would then move with someone else's
release schedule, the ids and messages would point readers at another project, and the bans and
the rules Trax adds would live in a second package. The copy costs a sync now and then, which
`upstream.txt` and the sync script make a readable diff.

Every rule the package ships lost because it ships rules that contradict each other (return the
task, await the task). The rules it leaves off are the ones its author treats as taste, so the
default set is the largest one that can be satisfied.

A curated list lost because it is the thing this replaces. A relaxed tier for tests lost for the
same reason. The cost is visible: an example quoted in the docs carries `ConfigureAwait(false)`
because the build requires it.

## Consequences

Moving to a newer upstream tag can fail the build, because a new version can enable new rules.
That pull request gives each new rule a line in `rules.txt` and fixes what it reports. Like any
dependency bumped by hand, the tag must be at least 7 days old.

## Exemplars

**Enforced elsewhere:** `AnalyzerProfileTests` in Trax.Core's Tests.Meta project requires every
.NET folder to import the profile, holds each `.globalconfig` line to a reason and a rule
`rules.txt` lists, and refuses every other way to turn a rule off, across the whole repository.
`ProfileTests` and `BanTests` in `Trax.Analyzers/tests/Trax.Analyzers.Package.Tests` hold the
generated configuration to `rules.txt` and require each ban to report.
`NoHandBuiltClockOrLoweringTests` in Trax.Api's and Trax.Scheduler's Tests.Meta projects refuse a
query that reaches the clock or a lowering by building the expression node itself, which the
profile cannot see. The build itself fails on any rule the profile reports.

## Changelog

- **2026-11-21**: Rewritten as the decision stands: the rules are a Trax-owned copy shipped as
  Trax.Analyzers, the profile is `rules.txt`, the rules Trax adds are marked there, and every
  folder, sample, template and the book is under it.
- **2026-11-17**: Added the team's layer: banned APIs, banned syntax, and no null-forgiving operator.
- **2026-11-17**: Recorded.
