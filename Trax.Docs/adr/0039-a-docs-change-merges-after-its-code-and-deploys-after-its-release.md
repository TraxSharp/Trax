---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs, website]
areas: [docs, ci]
status: superseded-by-0043
---

# A docs change describing new behaviour merges after its code, and its deploy waits for the release

A Trax.Docs change that describes new or changed behaviour merges only after the code change it
describes has merged, and the website deploy that would publish it is not approved until that code
is released to nuget.org. The published docs describe what a consumer can install. A page that
runs ahead of its package tells readers the package does something it does not.

## Status

**Superseded by** [0043](./0043-the-site-is-published-from-a-release.md): in one repository a docs
change lands with its code, and the site is published from each release tag.

## Why this is written down

A docs branch merged ahead of its code once told readers that a refusal existed, fail-closed
behaviour a host could rely on, while the released packages still allowed the case. Nothing about
the merge looked wrong: the code pull request was open and expected to land. For a
security-first library that is the worst direction to be wrong in, because a reader who trusts the
page skips the protection they would otherwise have added themselves.

Merging is not publishing in Trax. Code reaches consumers only when someone dispatches the release
workflow, so "the code merged" and "the behaviour exists for consumers" can be days apart, and a
docs merge timed to the code merge is still early.

## Considered options

**Merge docs and code together, in either order.** The workspace rule pairs every code change with
its docs change, and merging them as a pair looked sufficient. Rejected: the two are separate
repos with separate merges, so "together" is two events in some order, and the website does not
know about either.

**Version the docs per release.** Correct, but a docs site per package version is a large build for
a library with one consumer, and it does not remove the need to decide which version the default
page shows.

**Merge docs only after the release.** Simpler to state, but it leaves the docs branch open across
the release and lets it rot against other docs work. Merging after the code and holding the deploy
keeps the branch short-lived while still keeping the page off the site until the package exists.

## Consequences

**The deploy approval is the gate.** `trigger-website-deploy.yml` runs on every push to Trax.Docs
`main` (except ADR, tooling and test paths) and waits on the protected `website-deploy`
environment. Whoever approves it checks that every merged change it would publish describes
released code, and leaves it waiting when one does not.

**The hold covers everything on `main`, not one change.** Trax.Website clones Trax.Docs `main` at
build time, so any deploy, approved for an unrelated docs change or started from Trax.Website
itself, publishes every page merged so far. While a docs change waits for its release, every
deploy waits with it, or the docs change stays unmerged.

**A correction of existing text is not new behaviour.** A fix that makes a page match code that is
already released can merge and deploy at once.

## Exemplars

**Unenforced:** merge order across two repos and the timing of a release are not states any check
in one repository can see. The `website-deploy` environment in Trax.Docs is the manual
checkpoint, and the workspace `CLAUDE.md` ("Releases are manual") is where the release side is
described.

## Changelog

- **2026-10-06**: Superseded by 0043.
- **2026-10-01**: Recorded.
