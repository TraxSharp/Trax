---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [docs, testing]
status: accepted
---

# Documentation conventions are enforced by lint, not by review

The conventions governing `Trax.Docs` (no em-dashes, internal links that resolve, an SDK
reference block on every concept page with code, no leftover Jekyll syntax) are checked by
tests in CI rather than left to a reviewer.

## Status

**Accepted.**

## Why this is written down

Because the alternative looks reasonable and is not. `Trax.Docs` is 185 markdown files
rendered to a public site, and the failure modes are invisible at review time: a
`/docs/` link that points at a page nobody wrote is a 404 on traxsharp.net, and it reads
exactly like a working link in a diff. Nobody catches that by eye, and the person who would
notice is a user.

The em-dash rule is the one most likely to be mistaken for fussiness. It is a house-style
choice, and the reason it is machine-checked rather than requested is that it is
overwhelmingly likely to be violated by accident: autocorrect inserts them, and so does
generated prose.

## Considered options

**Review only.** What most documentation gets. With a single maintainer it means the author
reviews their own work, which catches style and never catches a broken cross-reference.

**An off-the-shelf markdown linter** (markdownlint, Vale). Rejected because the rules that
matter here are not generic: "every `/docs/x` resolves to `x.md` or `x/index.md` in this
repo" and "a concept page with code blocks carries an `## SDK Reference` block listing the
methods it used" are specific to how this site is built and rendered. A general linter
would need custom rules anyway, and would add a Node toolchain to a .NET repo.

## Consequences

**An exception is a recorded line, not a silence.** Both link and SDK-block guards carry an
explicit exceptions set with a justification per entry, so pre-existing debt is visible and
countable rather than invisible. Adding to those sets is a deliberate edit.

**The rules bind every repo, not just this one.** A change in any Trax repo is expected to
update `Trax.Docs` alongside it, so a contributor working in `Trax.Effect` is subject to
these lints the moment they touch a page.

## Exemplars

- `InternalLinksResolveTests` fails on a `/docs/` link with no target, which is the one that
  would otherwise ship a 404.
- `SdkReferenceBlockTests` requires the reference block on concept pages with code, and owns
  the exempt-folder list.
- `NoEmDashesTests` and `NoJekyllSyntaxTests` pin the house style and the migration away
  from Jekyll link syntax.

Not covered: nothing checks that a page is *correct*, only that it is well formed. A code
example that no longer compiles, or a described parameter that was renamed, passes every
one of these.

## Changelog

- **2026-09-11**: Recorded. The census asked which decision these four guards enforced, and
  there was no answer.
