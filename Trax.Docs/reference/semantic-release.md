---
layout: default
title: Semantic Release
description: "How Trax releases with semantic-release: commit types and the bump each cuts, running the release workflow by hand, the one shared version and unreleased work."
parent: Reference
nav_order: 5
---

# Semantic Release

Every Trax package (Trax.Core, Trax.Effect, Trax.Mediator, Trax.Scheduler, Trax.Api,
Trax.Dashboard, Trax.Cli and Trax.Samples.Templates) is released from the one
[TraxSharp/Trax](https://github.com/TraxSharp/Trax) repository, at **one shared version**, by
[semantic-release](https://github.com/semantic-release/semantic-release). Releases are cut by
hand. A merge to `main` runs no workflow and publishes nothing, since the pull request already built
and tested the change: work accumulates there until someone runs the release workflow, which runs
the full CI over every folder again before it releases everything merged since the last `v*` tag.

## Commit types

semantic-release reads the commits since the last `v*` tag with its default **angular** preset. The
squash merge uses the pull request title as the commit on `main`, so the PR title's type is what
counts:

```
type(scope): description
```

| Commit | Release |
|---|---|
| `feat:` | minor (1.4.2 → 1.5.0) |
| `fix:` | patch (1.4.2 → 1.4.3) |
| `perf:` | patch |
| `revert:` | patch, but only with `This reverts commit <sha>.` in the body, which `git revert` writes. A bare `revert: ...` subject releases nothing |
| `refactor:`, `docs:`, `test:`, `chore:`, `ci:`, `style:`, `build:` | none |
| `feat!:`, `fix!:` | **none**. The angular preset does not read `!`: the header does not parse, so the commit releases nothing |
| `BREAKING CHANGE:` in the body or footer | major |

The type decides what a release contains. A real fix committed as `refactor:` or `chore:` is
invisible to the analyzer, so running the workflow does not ship it and the package stays at the
old version. If that has already happened, an empty commit on `main` gives the next release
something to cut:

```bash
git commit --allow-empty -m "fix: <what the earlier commit fixed>"
```

A major release is permanent on NuGet. Do not write `BREAKING CHANGE:` in a commit unless a major
release is intended; describe a breaking change in the pull request instead.

## Cutting a release

1. Open the repository's **Actions** tab, choose **Release**, and **Run workflow** on `main`.
2. The `build-test` job runs the full CI (`.github/workflows/ci.yml`) over every folder.
3. The `release` job waits for approval on the protected `release` environment. Once approved,
   semantic-release reads every commit since the last `v*` tag and cuts **one** version at the
   highest bump those commits call for, with one set of release notes. It creates the git tag
   (`v1.61.0`) and the GitHub release, and writes the version to `.release-version`. If no commit
   calls for a release, it stops there.
4. The same job restores the locked dependencies of every folder and runs
   `dotnet pack -p:Version=<version>` on each, so every package, `Trax.Samples.Templates` included,
   carries that one version. It uploads the packages as an artifact.
5. The `publish` job, also gated on the `release` environment, attests the packages and pushes them
   to nuget.org with a short-lived Trusted Publishing key. See
   [Supply Chain Security](/docs/supply-chain-security).
6. The `website` job, gated on its own `website-deploy` environment, moves the `website` branch to
   the new tag, which publishes the site for that release.

The configuration lives in `.releaserc.json` at the repository root and the workflow in
`.github/workflows/release.yml`. The plugins are `commit-analyzer` (default rules),
`release-notes-generator`, `exec` (writes `.release-version`) and `github` (tag, release and a
comment on the released pull requests). There is no changelog or git plugin: no `CHANGELOG.md` is
written and nothing is committed back to `main`. The release notes are on the GitHub release.

No `Directory.Build.props` is updated. Each folder's stays at `1.99.99` permanently, which is the
version a local `dotnet pack` produces; the real version comes from the tag and is passed to
`dotnet pack` by the release.

The first shared release is 1.61.0. The tags from before the packages shared a version are kept,
prefixed with the folder name (`Trax.Core/v1.9.1`), and semantic-release never reads them.

## One version, no release order

Inside the repository the Trax packages reference each other with `ProjectReference`, so a change
that touches several packages builds and tests as one pull request, and one release ships all of it.
There are no Trax pins to bump between releases and no order to release in. A package whose code did
not change still gets the new version.

After a release, raise `PackageValidationBaselineVersion` in each `Directory.Build.props` that sets
it to the version just released, so package validation compares the next pack against it.

## Finding unreleased work

A merged fix that nobody releases never reaches a consumer. To see what is waiting:

```bash
git fetch --tags
git log --oneline "$(git describe --tags --abbrev=0 --match 'v*' origin/main)"..origin/main
```

Any `feat:`, `fix:`, `perf:` or `revert:` in that list is unreleased.

## Troubleshooting

**The workflow ran and nothing was released.** No commit since the last `v*` tag has a releasing type,
a commit used `feat!:`/`fix!:`, which the angular preset ignores, or a `revert:` has no
`This reverts commit <sha>.` line. Add an empty `fix:` or `feat:`
commit as above.

**The release job is waiting.** It needs an approval on the `release` environment.
