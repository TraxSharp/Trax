---
title: Writing docs
description: "The conventions for Trax docs pages and the tests that check them: front matter, page types, code fence markers, SDK reference pages, links and redirects."
type: reference
area: contributing
nav_order: 6
redirect_from:
  - /docs/reference/contributing-docs
---

# Writing docs

The pages are rendered by a Next.js site, not Jekyll. Tests in `Trax.Docs/tests` run in CI and fail
the build, so the conventions below are checked rather than requested. `Trax.Docs/adr/0008` records
why the conventions are linted, and `Trax.Docs/adr/0050` why pages are typed and their code is held
to the code.

## Front matter

Every published page opens with a YAML front matter block. Published means everything under
`Trax.Docs` except `README.md` files and the `adr/`, `tests/`, `tools/`, `.claude/` and `.github/`
folders, which is what `Trax.Website/scripts/sync-docs.sh` copies.

```yaml
---
title: Resolve a dead letter
description: "How to find a dead-lettered run, read why it failed, and requeue or discard it."
type: how-to
area: scheduling
nav_order: 3
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/DeadLetters
redirect_from:
  - /docs/scheduler/dead-letters-and-cleanup
---
```

| Field | Required | What it does |
|---|---|---|
| `title` | yes | The page's name in the sidebar and the browser tab. |
| `description` | yes | One plain sentence, at most 160 characters. |
| `type` | yes | The page's kind: `tutorial`, `how-to`, `reference`, `explanation` or `landing`. |
| `area` | yes | The subject the page belongs to, from the list below. |
| `nav_order` | no | The page's order within its group. |
| `source` | on SDK reference pages | Files or directories the page documents, relative to the repository root. The site links each one to GitHub. |
| `redirect_from` | no | Old `/docs/...` paths that now land on this page. |
| `parent`, `grand_parent`, `has_children` | no | Nest the page under the page whose `title` is `parent`. |
| `section` | no | The sidebar section, when it is not the one `type` implies. `none` puts the page at the top of the sidebar, under Home, outside every section. |
| `snippet_context` | no | `none` compiles the page's snippets without the shared context. |
| `follows` | no | The page whose project this one carries on, such as `book/an-order-is-a-train`. See [Compiled examples](#compiled-examples). |
| `package`, `namespace`, `surface` | on SDK reference pages | See [SDK reference pages](#sdk-reference-pages). |

`layout` is left over from Jekyll and the site ignores it; delete it from a page you edit.

`description` is the page's meta description and its line in `llms.txt`, which is what a search
result or an agent reads to decide whether to open the page. Write one plain-text sentence that
says what the reader finds on the page, naming the API or concept it covers: no markdown, no
backticks, no marketing. Quote the value when it contains `: `, as most do.

| Test | Fails on |
|---|---|
| `PageDescriptionTests` | a published page with no `description`, or one over 160 characters |
| `FrontMatterTests` | a published page with no `type` or `area`, a value outside the lists below, or an SDK reference page that is not `type: reference` |

### Types

A page is one of these, and only one. A page that mixes them is split, and the parts link to each
other. Each concept has one page that owns it; everywhere else gets a sentence and a link.

| `type` | The page is | Title |
|---|---|---|
| `tutorial` | A lesson: one path, a visible result at every step, minimal explanation, no alternatives. | What the reader builds |
| `how-to` | Steps toward a goal, for a reader who knows what they want. May branch; links out for the why and for option tables. | The goal, imperative: "Resolve a dead letter" |
| `reference` | A description of the thing and nothing else: neutral, tables, the shape of the code. Short examples. | The thing's name |
| `explanation` | Why and how it works: context, design, trade-offs, alternatives. No steps, no option tables. | A noun phrase, or "How X works" |
| `landing` | An overview whose job is routing: what the area is, then its pages grouped by type. | The area |

The sidebar puts a page in the section its type implies (Tutorials, How-to guides, Explanation,
Reference) unless `section` says otherwise, and the page shows its type above its title. Pages in
the `samples` and `contributing` areas sit in sections of their own.

### Areas

`area`, in the order the sidebar groups them: `general`, `trains`, `effects`, `data`, `mediator`,
`scheduling`, `api`, `security`, `machines`, `dashboard`, `testing`, `tooling`, `upgrading`,
`samples`, `contributing`. A page has one area. At its foot, a page lists the other pages of its
area, grouped by type.

## Links

Internal links are direct `/docs/` paths, relative to the docs root rather than to the
current file:

```markdown
[Registration Order](/docs/reference/registration-order)
[Metadata](/docs/effect/metadata#what-gets-persisted)
```

Jekyll template syntax is rejected: the link tag, the include tag, the baseurl variable, and
kramdown inline attribute lists. All four are leftovers from the site's Jekyll era and render
as literal text now. The lint names them precisely; this page does not spell them out, because
writing one would trip the lint it describes.

Every `/docs/` link must resolve to a published page, and its `#anchor`, if it has one, to a
heading on that page. A broken one is a 404 on the live site and reads exactly like a working link
in a diff, which is why `InternalLinksResolveTests` checks it:

- A link is `/docs/...`, a same-page `#anchor`, or a full URL with a scheme. A relative link such
  as `delayed-jobs.md` is rejected: the site resolves it against the page URL, so it lands on raw
  markdown or a 404.
- `/docs/foo/bar` resolves to `foo/bar.md` only. A `foo/index.md` is served at `/docs/foo/index`,
  not `/docs/foo`.
- A path a page claims in `redirect_from` resolves to that page, as it does on the site, and its
  anchor is checked against that page. Write new links to the page itself.
- An anchor is the heading's id as the site generates it (github-slugger): lowercased, punctuation
  dropped, spaces turned into hyphens, and `-1`, `-2` appended to repeats. `## 1. Retry with
  Exponential Backoff` is `#1-retry-with-exponential-backoff`.

A link that cannot be fixed yet goes in the test's `KnownBrokenLinks`, keyed by page and target
with a reason. An entry whose link has been fixed fails the test until it is deleted.

### Redirects

A page that moves or is renamed lists its old paths in `redirect_from`, and the site turns each
one into a permanent redirect. `RedirectsTests` checks that each entry starts with `/docs/`, carries
no anchor or query, is not the path of a page that still exists (the redirect would hide it), and
is claimed by one page only.

### Code and docs link both ways

A page points at the code it documents with `source:`. The code points back with an XML doc
comment on the type the page covers:

```csharp fragment
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/schedule">SDK reference</seealso>
```

An analyzer diagnostic sets its `helpLinkUri` to the page that explains it. In prose, link to code
with a full GitHub URL only when `source:` cannot carry it.

| Test | Fails on |
|---|---|
| `CodeLinksToDocsResolveTests` | a `https://traxsharp.net/docs/...` link in a `*.cs` file under a `Trax.*/src/` folder, `Trax.Samples/samples/` or `Trax.Samples/templates/` that does not resolve to a published page (or a `redirect_from` of one), or whose anchor names no heading on it |
| `SourceLinksBackTests` | an SDK reference page whose `source:` lists C# files, none of which links to the page |

## SDK reference pages

A page under `sdk-reference/<folder>/` documents one member or type and is `type: reference`.
`sdk-reference.md` and `sdk-reference/<folder>.md` are the indexes. Each page states what it
documents in its front matter, and the site renders it as a line under the title:

```yaml
package: Trax.Scheduler
namespace: Trax.Scheduler.Services.TraxScheduler
source:
  - Trax.Scheduler/src/Trax.Scheduler/Services/TraxScheduler/ITraxScheduler.cs
```

| Field | Value |
|---|---|
| `package` | The project name of a packable project: a csproj under a `Trax.*/src/` folder that does not set `IsPackable` false. |
| `namespace` | The namespace the type is in, or a list of them. Not needed with `surface`. |
| `source` | The files or directories the page documents, relative to the repository root. |
| `surface` | `graphql` for the GraphQL schema pages (`package: Trax.Api.GraphQL`), `typescript` for the npm package (`package` then names it, as its `package.json` does). |

`SdkReferenceFrontMatterTests` checks that `package` is a packable project, that each `namespace`
is declared in that package's public API baseline (`Trax.*/tests/*.Tests.Meta/PublicApi/`) when it
has one, and that each `source` path exists. It also reads the fences under `## Signature` or
`## Signatures`, and requires every type name they declare (after `class`, `interface`, `record`,
`struct`, `enum`, `delegate`) and every member name (before its parameter list, or before
`{ get`) to be in that baseline.

The headings, in this order, leaving out what does not apply:

```markdown
# <Member or Type>
<one paragraph: what it is>
## Signature        (or Signatures; fences marked fragment)
## Parameters       (a table; a type page has ## Members or ## Properties)
## Returns
## Behavior         (what it does, what it throws, what it registers)
## Example          (or Examples; fences marked compile)
## See also         (the how-to, the explanation, sibling reference pages)
```

A type page may add an H2 per member. There is no `## Package` section, since the front matter
carries it, and no `## SDK Reference` block. A heading says what the thing is, not whether to use
it: "Recommended" or "Legacy" is one neutral line under `## Behavior`. What a method registers goes
under `## Behavior`, as a `### Registered services` H3 when it needs a table.

## SDK reference blocks

A page outside `sdk-reference/` that contains a fenced code block carries a consolidated block
listing the SDK methods its examples use:

```markdown
## SDK Reference

> [AddTrax](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
```

`SdkReferenceBlockTests` checks one thing: that such a page has an `## SDK Reference` heading
somewhere in it. Position, count, separator and what the links point at are convention, held
up by review rather than by the lint.

The lint exempts anything under `sdk-reference/`, `migration-guides/`, `adr/` or `.claude/`; the
filenames that are tables of contents wherever they appear (`README.md`, `index.md`, and the
per-area overview pages such as `effect.md`); and a list of named paths. Most of those are pages
whose code is shell commands, SQL, directory trees or csproj fragments, with no SDK method to link
to, and this page is one of them. The rest are tracked debt: concept pages that should carry a
block and do not. A new page is not added to that list.

## Code in documentation

Include code where it helps. Configuration examples, user-facing API usage, interface
signatures and focused snippets all earn their place.

Do not paste whole method bodies or class implementations. They drift from reality as the
code changes and are denser than a reader needs: show the five lines that make the point, not
the forty-line method. When documenting a model's fields, use a table rather than pasting the
class.

### Fence markers

Every C# fence (language `csharp`, `cs` or `c#`) on a published page carries exactly one marker
after the language, which says how the fence relates to the code. The site ignores the extra
words, so readers do not see them.

| Marker | The fence is | Checked by |
|---|---|---|
| `compile`, `compile=group[,group]` | copy-complete, and compiled in CI | `CompiledSnippetsTests` |
| `from=<path>[#region]` | an excerpt of a real file, which it must match | `SampleExcerptsMatchSourceTests` |
| `fragment` | partial on purpose: a signature, an interface copied for reference, a before and after, a line out of context | nothing; it is a declaration |

~~~markdown
```csharp compile
var every5Minutes = Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2));
```

```csharp from=Trax.Samples/samples/ChatService/Trax.Samples.ChatService.Api/Program.cs#docs-setup
builder.Services.AddTrax(trax => trax
// ...
);
```

```csharp fragment
Task<Manifest> ScheduleAsync<TTrain, TInput, TOutput>(string externalId, TInput input, Schedule schedule);
```
~~~

Use `compile` on tutorial and how-to pages and on every SDK reference `## Example`, `from=` on
sample pages, and `fragment` for signatures and code that is partial by nature. `compile` is C#
only; `from=` may also go on a fence in another language taken from a real file (a csproj, JSON,
TypeScript, a shell script).

`EveryCSharpFenceIsMarkedTests` fails on a C# fence with no marker, on a fence with two, and on a
`compile` fence under a `## Signature` or `## Signatures` heading (a level 3 heading stays inside
the section).

### Compiled examples

A `compile` fence is compiled in CI against the Trax projects of the same checkout, which
`tests/Trax.Docs.Snippets.Tests` references as projects: every packable library, so any page can
be compiled.

| Marker | Compiles |
|---|---|
| `compile` | this fence on its own |
| `compile=app` | with every fence on the same page marked `compile=app`, as the files of one project |
| `compile=app,api` | in each of the named groups, for a file two stages of a walkthrough share |

Each fence is its own source file. The compilation has the implicit usings of a
`Microsoft.NET.Sdk.Web` project and nullable reference types on, and nothing from Trax is implied:
a page that shows a whole file shows its `using` lines. A group with top-level statements compiles
as an executable, so one fence per group can be a `Program.cs`. Warnings fail the build as errors
do, and each diagnostic names the page and the line on it.

Context files in `tests/Trax.Docs.Snippets.Tests/Context/` join a page's compilations, and the
reader never sees them. They layer, widest first:

| File | Joins | Holds |
|---|---|---|
| `_shared.cs` | every page | The host a snippet may assume, as static members: `builder`, `services`, `app`, `configuration`, `connectionString`, `rabbitMqConnectionString`, `ct`, `cancellationToken`, `serviceProvider`, `assembly`, `assemblies`. No Trax usings. |
| `<folder>/_section.cs` | every page under that folder, at any depth | Sample types a whole section's examples share. `global using Trax.*` lines only under `sdk-reference/`. |
| `<page path>.cs` | that page (`Context/sdk-reference/scheduler-api/schedule.cs`) | The types the page only implies, such as the `ISyncTrain` a scheduling example schedules, and `global using` lines for the namespaces its examples leave out. |

A page can carry on the project another page started, as each chapter of the book carries on
the last. Its front matter names that page, `follows: book/an-order-is-a-train`, and each of its
groups then compiles with the followed page's group of the same name, back to the first page of
the chain. Every compiled fence on such a page, and on a page another one follows, names the file
it shows:

~~~markdown
```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
```
~~~

A fence replaces the fence before it in the chain that names the same file, so a chapter shows
only the files it adds or changes and is still compiled as the whole project. Diagnostics name the
page each fence is on. `FollowedPagesTests` fails on a `follows:` that names no published page or
comes back on itself, on a fence in a chain that names no file or names one twice in a group, and
on `file=` on a C# fence that is not compiled. A fence in another language may name its file too
(`xml file=Shopfront.csproj`), so the page records every file of the project, not only the C# ones.

A page that moves files lists them under `moves:` in its front matter, each a path prefix of the
files the pages before it showed and the prefix they move to, as a chapter that splits one project
into several does:

```yaml
moves:
  - Trains/ -> Shopfront.Core/Trains/
  - Program.cs -> Shopfront.Hub/Program.cs
```

A moved file keeps its fence; a fence on this page or a later one that names the new path replaces
it. On a page in a chain, a group other than `app` is a project of its own that references the
`app` group, as a worker references the library its trains are in, and compiles as an assembly of
its own against it.

A group named `tests` is the project's test project. It compiles as an assembly of its own against
the same page's `app` group, as a test project compiles against the project it references, and
sees that group's `internal` types as `InternalsVisibleTo` would let it. It gets the implicit
usings of a plain `Microsoft.NET.Sdk` project, not the Web SDK's, and it is the only group that
sees the test frameworks a reader's test project has: NUnit, AwesomeAssertions and
`Microsoft.AspNetCore.Mvc.Testing`.

A snippet that declares its own `builder` or `services`, as a `Program.cs` does, hides the shared
one. A page whose front matter says `snippet_context: none` gets only its own file, so it compiles
as a reader's empty project would; `getting-started.md` does, because it is the page a reader
copies whole. `CompiledSnippetsTests` fails on a context file whose page compiles nothing, a
`_section.cs` with no compiled page under it, and a Trax `global using` in `_shared.cs` or in a
`_section.cs` outside `sdk-reference/`.

### Excerpts

A `from=` path is relative to the repository root, the folder holding `Trax.Docs`. `#name` selects
the lines between `#region name` and its `#endregion` in a C# file, `// #region name` and
`// #endregion` in TypeScript or JavaScript, or `<!-- #region name -->` and `<!-- #endregion -->`
in XML; without it the whole file is the region. Adding `#region docs-<name>` markers to a sample
file is expected.

The fence is split into chunks at lines that are exactly `// ...` (or `# ...`, `<!-- ... -->`).
Each chunk must occur in the region as consecutive lines, the chunks in the order the fence shows
them. Trailing whitespace and the indentation a chunk's lines share are not compared, so a chunk
lifted out of a nested block drops its outer indentation. Region marker lines are ignored on both
sides. A failure names the page and line, the file, and the first chunk that did not match.

### Package versions

Every Trax `PackageReference` with a `Version` on a page, and every Trax `PackageVersion` in a
`Directory.Packages.props` it shows, names one exact version, the same on
every page, because every Trax package releases at one version; `DocsPackageVersionsTests` lists
each one that differs. A production build of the site stamps the latest release version into each
of them, so a reader installs the release the site was published from.

## Lists of pages that predate a rule

A rule added after the pages it covers lists the pages that break it in a text file beside its
test: one page path per line, relative to `Trax.Docs`, in ordinal order, `#` lines are comments.
Each list only shrinks. A listed page the rule no longer catches, or that no longer exists, fails
the test until its line is deleted, so fixing a page means deleting its line too, and moving a page
means updating its line.

| File | Rule |
|---|---|
| `tests/Trax.Docs.Snippets.Tests/KnownUnmarkedFencePages.txt` | every C# fence carries a marker |
| `tests/Trax.Docs.Tests/KnownIncompleteSdkPages.txt` | an SDK reference page states `package`, `namespace` and `source` |
| `tests/Trax.Docs.Tests/KnownSignatureMismatches.txt` | a signature declares only what the package's baseline has |
| `tests/Trax.Docs.Tests/KnownUnlinkedSdkPages.txt` | an SDK reference page's source links back to it |
| `tests/Trax.Docs.Tests/KnownUndocumentedPublicApi.txt` | every public type and member is named on a page (`PublicApiIsDocumentedTests`) |

Never add a line to one of them: fix the page instead.

## Voice

Write like a senior engineer talking to other engineers.

- **No em-dashes.** Use commas, periods or parentheses. They leak in from autocorrect and
  from generated prose, which is why this one is checked (`NoEmDashesTests`).
- **No filler.** Not "ensure", "leverage", "enhance", "it is worth noting", "this allows you
  to". Say what the thing does.
- **Tables for options.** Properties and configuration go in table rows inside their parent
  section, not in standalone sections.
- **Match the neighbours.** Read the adjacent pages before writing, and follow their shape.
