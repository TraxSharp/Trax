---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs, website]
areas: [docs, ci]
status: accepted
---

# The site is published from a release, and a docs change lands with its code

A docs change ships in the same pull request as the code it describes. traxsharp.net is built
from the `website` branch, which the release workflow moves to each release tag once that
release's packages are on nuget.org, so the published docs describe released code and nothing
merged after it. The build stamps the release's version into every Trax package reference a
page shows.

## Status

**Accepted.** Supersedes
[0039](./0039-a-docs-change-merges-after-its-code-and-deploys-after-its-release.md), which
held a docs change back until its code merged and held every deploy behind a manual approval
until the code was released, because the docs and the code were separate repositories.

## Considered options

**Deploy `main` on every merge, as before.** A docs change now lands with its code, so `main`
always describes `main`. But `main` is not what a reader can install: releases are dispatched
by hand, so a page could describe a refusal days before any package enforces it, the case
0039 was written for.

**Keep an approval on every deploy of `main`.** What 0039 did. It made the approver remember
which merged changes were released, and held every unrelated fix behind the slowest one.

## Consequences

**A docs-only fix waits for the next release**, unless it describes nothing unreleased: then
dispatch the Website workflow with `main`, which publishes everything merged so far. The
check that nothing merged is unreleased is the dispatcher's.

**A page's written version does not matter for the site.** The build stamps the latest
release's version over it; `DocsPackageVersionsTests` only keeps the pages agreeing with each
other.

## Exemplars

**Enforced elsewhere:** `.github/workflows/release.yml` runs `website.yml` with the tag it
cut, after the publish job, and `website.yml` refuses any ref that is not a release tag or
`main`. `Trax.Website/scripts/sync-docs.sh` stamps the version in a production build and fails
on a version that is not `X.Y.Z`.

Not covered: nothing stops a dispatch of `main` while unreleased behaviour is documented
there, and Vercel's production branch being `website` is a project setting no file records.

## Changelog

- **2026-10-06**: Recorded.
