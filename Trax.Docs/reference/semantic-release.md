---
layout: default
title: Semantic Release
description: "How Trax repos release with semantic-release: commit types and the bump each cuts, running the release workflow by hand, release order and unreleased work."
parent: Reference
nav_order: 5
---

# Semantic Release

Every Trax repo that publishes to NuGet (Trax.Core, Trax.Effect, Trax.Mediator, Trax.Scheduler,
Trax.Api, Trax.Dashboard, Trax.Cli and Trax.Samples) versions its releases with
[semantic-release](https://github.com/semantic-release/semantic-release). Releases are cut by
hand. A merge to `main` runs no workflow and publishes nothing, since the pull request already built
and tested the change: work accumulates there until someone runs the release workflow, which builds
and tests `main` again before it releases everything merged since the last tag.

## Commit types

semantic-release reads the commits since the last tag with its default **angular** preset. The
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

1. Open the repo's **Actions** tab, choose **Release NuGet Package**, and **Run workflow** on
   `main`.
2. The `build-test` job builds and tests `main`.
3. The `release` job waits for approval on the protected `release` environment. Once approved,
   semantic-release reads every commit since the last tag and cuts **one** version at the highest
   bump those commits call for, with one set of release notes. It creates the git tag (`v1.5.0`) and
   the GitHub release, and writes the version to `.release-version`. If no commit calls for a
   release, it stops there.
4. The same job restores the locked dependencies and runs
   `dotnet pack -p:Version=<version>`, uploading the packages as an artifact.
5. The `publish` job, also gated on the `release` environment, attests the packages and pushes them
   to nuget.org with a short-lived Trusted Publishing key. See
   [Supply Chain Security](/docs/supply-chain-security).

The configuration lives in each repo's `.releaserc.json` and the workflow in
`.github/workflows/nuget_release.yml`. The plugins are `commit-analyzer` (default rules),
`release-notes-generator`, `exec` (writes `.release-version`) and `github` (tag, release and a
comment on the released pull requests). There is no changelog or git plugin: no `CHANGELOG.md` is
written and nothing is committed back to `main`. The release notes are on the GitHub release.

`Directory.Build.props` is **not** updated. It stays at `1.99.99` permanently, which is the version
a local pack produces; the real version comes from the tag and is passed to `dotnet pack` by CI.

## Release order

The repos depend on each other through exact package pins in `Directory.Packages.props`, and CI
restores in locked mode. So a downstream repo builds against the last *published* upstream version,
not against upstream `main`. Release upstream first, one repo at a time:

```
Trax.Core → Trax.Effect → Trax.Mediator → Trax.Scheduler → Trax.Api / Trax.Cli → Trax.Dashboard → Trax.Samples
```

Between steps, bump the downstream repo's pin to the version just released and regenerate its
lockfiles. A change that spans repos therefore needs the upstream release in the middle: the
downstream half cannot pass CI until the upstream half is on nuget.org.

After releasing Trax.Core, raise `PackageValidationBaselineVersion` in its `Directory.Build.props`
to the version just released, so package validation compares the next pack against it.

## Finding unreleased work

A merged fix that nobody releases never reaches a consumer. To see what is waiting in a repo:

```bash
git fetch --tags
git log --oneline "$(git describe --tags --abbrev=0 origin/main)"..origin/main
```

Any `feat:`, `fix:`, `perf:` or `revert:` in that list is unreleased.

## Troubleshooting

**The workflow ran and nothing was released.** No commit since the last tag has a releasing type,
a commit used `feat!:`/`fix!:`, which the angular preset ignores, or a `revert:` has no
`This reverts commit <sha>.` line. Add an empty `fix:` or `feat:`
commit as above.

**The release job is waiting.** It needs an approval on the `release` environment.

**A downstream build fails on restore after an upstream release.** The pin was bumped before the
package reached nuget.org, or the lockfiles were not regenerated. Wait for the package to be
listed, then restore again.
