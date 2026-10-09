---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs]
areas: [docs, testing]
status: accepted
---

# Documentation conventions are enforced by lint, not by review

The conventions governing `Trax.Docs` (no em-dashes, internal links that resolve, an SDK
reference block on every concept page with code, no leftover Jekyll syntax, C# examples marked
to compile that do compile against the published packages, a description in every page's front
matter, every public API and every migration named on a page) are checked by tests in CI rather
than left to a reviewer.

## Status

**Accepted.**

## Why this is written down

Because the alternative looks reasonable and is not. `Trax.Docs` is around two hundred markdown files
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
matter here are not generic: "every `/docs/x#y` resolves to `x.md` in this repo and to a
heading on it whose github-slugger id is `y`" and "a concept page with code blocks carries an `## SDK Reference` block listing the
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
  would otherwise ship a 404, on an anchor with no heading, and on a relative link, which the
  site resolves against the page URL.
- `SdkReferenceBlockTests` requires the reference block on concept pages with code, and owns
  the exempt-folder list.
- `NoEmDashesTests` and `NoJekyllSyntaxTests` pin the house style and the migration away
  from Jekyll link syntax.
- `CompiledSnippetsTests` compiles every fence marked `csharp compile` against the published
  Trax packages pinned in `tests/Trax.Docs.Snippets.Tests`, warnings included, and names the
  page and line of each diagnostic. `DocsPackageVersionsTests` holds a Trax version written on a
  page to the pin the snippets compiled against, and refuses a floating `1.*`.
- `PackagesPageTests` holds `reference/packages.md` to the list of published packages the test
  owns, in both directions.
- `PageDescriptionTests` requires a one-sentence `description` in every published page's front
  matter, no longer than the 160 characters at which the site truncates it.
- `PublicApiIsDocumentedTests` requires every public type and member in each folder's public API
  baseline to be named on a published page, as a reader would search for it (`runGraph` for
  `GetRunGraph`, `NOT_REACHED` for `NotReached`). The dashboard's Blazor components and the EF
  mappings `PersistentX` follow their documented type; the API that predated the guard is listed in
  `tests/Trax.Docs.Tests/KnownUndocumentedPublicApi.txt`, which may only shrink.
- `MigrationsAreDocumentedTests` requires every Postgres and SQLite migration to be named in the
  database migrations guide, or recorded in its `NotInTheGuide` set with the reason it changes
  nothing an upgrader sees.

Not covered: compiling is opt in, and most fences are not marked. The public API guard checks
that a name appears on some published page, not that it appears on the right one, nor that what the
page says about it is true; a member whose name is a common word (`Id`, `Name`) always passes. An unmarked example that no
longer compiles, a described parameter that was renamed, or a compiled example whose prose says
something the code does not do, passes every one of these. A compiled snippet proves the API
exists in the pinned release, not that the example behaves as the page says.

## Changelog

- **2026-10-09**: Added the public API and migration guards. A milestone shipped a testing type
  whose documented checks were three of eight, five migrations missing from the upgrade guide, and
  public properties no page named, and no test noticed.
- **2026-10-01**: Added the page description guard. The site's meta description and `llms.txt`
  fell back to each page's first paragraph, which is often a notice or a lead-in to a code block.
- **2026-10-01**: The link guard now rejects relative links and checks anchors, and no longer
  accepts `x/index.md` for `/docs/x`, which the site never served there.
- **2026-10-01**: Added the compiled-snippet and package-version guards. "Nothing checks that
  an example compiles" was the gap that let the getting-started page ship code that failed on
  its first line; marked fences now close it, and the gap that remains is the unmarked ones.
- **2026-09-11**: Recorded. The census asked which decision these four guards enforced, and
  there was no answer.
