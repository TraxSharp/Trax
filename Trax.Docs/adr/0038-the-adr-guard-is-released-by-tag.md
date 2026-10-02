---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [ci]
status: accepted
---

# The ADR guard is released by tag, and the code repos pin the tagged commit

The `adr-guard` composite action is released by cutting a `vX.Y.Z` tag on Trax.Docs, by hand,
through the `Release ADR Guard` workflow. Each code repo pins the tagged commit's full SHA with
the version in a trailing comment (`adr-guard@<sha> # v1.0.0`), and its existing Dependabot
`github-actions` entry moves the pin when a newer tag appears. `v*` tags on Trax.Docs belong to
the guard and to nothing else.

## Status

**Accepted.**

## Why this is written down

The eight code repos pinned the action at a bare commit SHA, and Trax.Docs had no tags. A bare
SHA tells Dependabot nothing about versions, so every guard fix needed eight hand-made PRs, and
Trax.Docs' own CI runs the action from its local path, so no consumer exercised a fix until
someone remembered to bump it. The pins sat at one commit while the fixes they were waiting
for accumulated on `main`.

## Considered options

**A step in the workspace release checklist: "bump the adr-guard pin in all eight repos."**
Rejected because it depends on someone remembering, which is what had already failed, and the
checklist lives in an unversioned file a contributor cloning one repo never sees.

**Pin the code repos to a branch or a floating `v1` tag.** Rejected because every other action
in these repos is pinned to a full SHA, as supply-chain hygiene, and a moving reference would
let a Trax.Docs merge change eight repos' CI with no PR in any of them. A floating major tag
also adds nothing once Dependabot is doing the moving.

**Tag automatically on every merge that touches the guard.** Rejected to match the manual
release model the packages follow: a tag opens a Dependabot PR in eight repos, and a stricter
check can fail a consumer's corpus. Choosing when that happens is a decision, and the workflow
waits on the protected `release` environment like a package release does.

**A prefixed tag (`adr-guard-v1.0.0`).** Rejected because Dependabot reads the version from
the tag name, and a plain `vX.Y.Z` is the form it resolves without surprises. The cost is that
the namespace is taken: see Consequences.

## Consequences

The bump is chosen by what a caller sees. **Patch** is a fix that fails no corpus the previous
release passed. **Minor** is a new or stricter check, which can fail a corpus that passed: the
Dependabot PR in that repo runs the new guard against it, so the PR is where the corpus gets
fixed. **Major** is an input removed, renamed or given a different default, which breaks the
`with:` block rather than the corpus.

The workflow refuses to tag when nothing the action runs (`.github/actions/adr-guard`,
`tools/Trax.Adr.Guard`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`) has
changed since the last tag, because a tag over a
docs-only change would open eight PRs that change nothing.

Dependabot resolves an action in a subdirectory (`TraxSharp/Trax.Docs/.github/actions/adr-guard`)
against the tags of the whole repository. Any other version-shaped tag on Trax.Docs would be
offered to the code repos as a guard release, so if the docs are ever versioned, those tags
take a different shape.

A code repo's pin must name the commit the tag points at. Dependabot recognises the current
version by the tag on that commit and rewrites both the SHA and the comment; a SHA that no tag
points at gives it nothing to compare.

## Exemplars

**Enforced elsewhere:** `.github/workflows/release-adr-guard.yml` in this repo computes the
next tag from the last one and the chosen bump, runs the guard's tests first, and refuses a
release with no change to what the action runs. Each code repo's `.github/dependabot.yml`
`github-actions` entry opens the PR that moves its pin.

Not covered: nothing checks that a code repo's pin carries the version comment or names a
tagged commit, and nothing checks that the bump chosen matches what changed.

## Changelog

- **2026-10-01**: Recorded.
