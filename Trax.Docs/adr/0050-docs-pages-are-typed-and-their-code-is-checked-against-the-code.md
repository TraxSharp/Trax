---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples, docs, website]
areas: [docs, testing]
status: accepted
---

# Docs pages are typed, and their code is checked against the code

Every published page declares one Diátaxis type (tutorial, how-to, reference, explanation, or
landing) and one area. Every C# fence says how it relates to the code: `compile` (it builds),
`from=<path>` (it is an excerpt of a real file, and must match it) or `fragment` (it is partial on
purpose). An SDK reference page names its package, namespace and source files, and those are
checked against the checkout and the public API baselines. Code links to its docs page and the
page links to its code, and both directions are checked.

## Status

**Accepted.**

## Why this is written down

The docs grew by package, and most pages ended up teaching, instructing and listing options at
once, so a reader looking for one of those reads all three. A type per page is what forces the
split, and the site's sidebar and page chrome now depend on it, which makes it expensive to undo.

The code on the pages had one check, opt in: of about a thousand C# fences, twelve compiled. The
rest read the same whether they were checked last week or had drifted from the API a year ago.
Requiring a marker does not make every fence compile; it makes the author say which kind it is,
and each kind has a check that fits it.

## Considered options

**Keep marking opt in.** Rejected: an unmarked fence carries no claim, so nothing can fail it, and
nobody can tell a deliberate fragment from a forgotten example.

**Compile every fence.** Rejected: signatures, interface copies and before-and-after pairs do not
compile on their own, and making them compile means padding the page with declarations the reader
does not need. A signature is checked against the public API baseline instead, which is what it
claims to match.

**Generate the reference from XML doc comments** (DocFX or similar). Rejected: it adds a second
toolchain and site, and loses the hand-written parameters, behaviour and examples that make the
reference useful. The baselines already hold the public API, so the pages can be checked against
them without being generated from them.

**Organise the docs by package and leave the type implicit.** What the docs did before. Rejected
because a package boundary is not a reader's question: a how-to for scheduling a train touches four
packages.

## Consequences

**Every page rule that arrives after the pages has a list of the pages that predate it**, in a text
file beside the test, and the list only shrinks. A page that is fixed takes its line with it; a
page that moves updates its line.

**A sample edit can fail the docs build.** A sample tour quotes the sample with `from=`, so
changing the quoted lines without touching the page fails `SampleExcerptsMatchSourceTests`. That
is the point; the fix is to re-copy the lines or move the `#region` markers.

## Exemplars

- [Writing docs](/docs/contributing/writing-docs) is the rule this produces: the fields, the
  markers, the template and the lists.
- `FrontMatterTests` requires `type` and `area` on every published page, from fixed lists.
- `EveryCSharpFenceIsMarkedTests` requires one marker on every C# fence, and no `compile` under a
  `## Signature` heading.
- `CompiledSnippetsTests` compiles `compile` fences with the layered context, and keeps Trax usings
  out of the shared layers.
- `SampleExcerptsMatchSourceTests` holds every `from=` fence to the file it names.
- `BookSampleIsTheBookTests` holds `Trax.Samples/book/Shopfront` to the files the book's last page
  leaves the reader with, so the sample's suite runs the book's own tests.
- `SdkReferenceFrontMatterTests` checks an SDK reference page's package, namespace and source, and
  the names its signatures declare, against the checkout and the baselines.
- `RedirectsTests` checks `redirect_from` claims; `CodeLinksToDocsResolveTests` resolves every
  docs link in shipped code; `SourceLinksBackTests` requires a page's source to link back to it.

Not covered: the type is declared, not verified, so a page marked `how-to` that teaches passes. A
`fragment` fence is checked by nothing, and `compile` proves an example builds, not that it does
what the page says. The signature check reads declared names only, so a wrong parameter type or
order passes, and it runs only for packages with a baseline. A page's `namespace` is checked to
exist in its package, not to be the one the documented type is in.

## Changelog

- **2026-10-19**: `BookSampleIsTheBookTests` added, with the book's project as a sample.
- **2026-10-13**: Recorded.
