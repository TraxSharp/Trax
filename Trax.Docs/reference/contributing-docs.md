---
layout: default
title: Contributing to the Docs
description: "Conventions for writing Trax docs and the lints that enforce them: absolute /docs links, front matter, SDK reference blocks, voice and compiled examples."
parent: Reference
nav_order: 14
---

# Contributing to the Docs

These pages are rendered by a Next.js site, not Jekyll. Lints run in CI and fail the build, so
the conventions below are checked rather than requested.

## Front matter

Every page opens with a YAML front matter block:

```yaml
---
layout: default
title: Delayed / One-Off Jobs
description: "Running work once, later: TriggerAsync with a delay on an existing manifest, or ScheduleOnceAsync for one-off manifests that disable themselves after running."
parent: Scheduling
nav_order: 3
---
```

`title` names the page in the sidebar and the browser tab. `parent`, `grand_parent`, `nav_order`
and `has_children` place it in the navigation tree; `parent` matches the parent page's `title`.
A top-level page also takes a `section`, the sidebar heading it sits under.
`layout` is left over from Jekyll and the site ignores it.

`description` is the page's meta description and its line in `llms.txt`, which is what a search
result or an agent reads to decide whether to open the page. Write one plain-text sentence that
says what the reader finds on the page, naming the API or concept it covers: no markdown, no
backticks, no marketing. Keep it to 160 characters, where the site truncates it. Quote the value
when it contains `: `, as most do. `PageDescriptionTests` fails on a published page with no
description, or one over 160 characters.

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

Every `/docs/` link must resolve to a real file, and its `#anchor`, if it has one, to a heading
on that page. A broken one is a 404 on the live site and reads exactly like a working link in a
diff, which is why `InternalLinksResolveTests` checks it:

- A link is `/docs/...`, a same-page `#anchor`, or a full URL with a scheme. A relative link such
  as `delayed-jobs.md` is rejected: the site resolves it against the page URL, so it lands on raw
  markdown or a 404.
- `/docs/foo/bar` resolves to `foo/bar.md` only. A `foo/index.md` is served at `/docs/foo/index`,
  not `/docs/foo`.
- An anchor is the heading's id as the site generates it (github-slugger): lowercased, punctuation
  dropped, spaces turned into hyphens, and `-1`, `-2` appended to repeats. `## 1. Retry with
  Exponential Backoff` is `#1-retry-with-exponential-backoff`.

A link that cannot be fixed yet goes in the test's `KnownBrokenLinks`, keyed by page and target
with a reason. An entry whose link has been fixed fails the test until it is deleted.

## SDK reference blocks

A page that contains a fenced code block carries a consolidated block listing the SDK methods
its examples use:

```markdown
## SDK Reference

> [AddTrax](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
```

`SdkReferenceBlockTests` checks one thing: that such a page has an `## SDK Reference` heading
somewhere in it. Position, count, separator and what the links point at are convention, held
up by review rather than by the lint.

The lint exempts, in order: anything under `sdk-reference/`, `migration-guides/`, `adr/` or
`.claude/`; thirteen filenames that are tables of contents wherever they appear (`README.md`,
`index.md`, and the per-area overview pages such as `effect.md`); and twelve named paths.
Those last are not a licence to skip the block. Nine are pages whose code is shell commands,
SQL, directory trees or csproj fragments, with no SDK method to link to, and this page is one
of them. The other three are tracked tech debt: concept pages that should carry a block and do
not. A new page is not added to that list.

## Voice

Write like a senior engineer talking to other engineers.

- **No em-dashes.** Use commas, periods or parentheses. They leak in from autocorrect and
  from generated prose, which is why this one is checked.
- **No filler.** Not "ensure", "leverage", "enhance", "it is worth noting", "this allows you
  to". Say what the thing does.
- **Tables for options.** Properties and configuration go in table rows inside their parent
  section, not in standalone sections.
- **Match the neighbours.** Read the adjacent pages before writing, and follow their shape.

## Code in documentation

Include code where it helps. Configuration examples, user-facing API usage, interface
signatures and focused snippets all earn their place.

Do not paste whole method bodies or class implementations. They drift from reality as the
code changes and are denser than a reader needs: show the five lines that make the point, not
the forty-line method. When documenting a model's fields, use a table rather than pasting the
class.

## Compiled examples

A C# fence whose info string carries `compile` after the language is compiled in CI against the
Trax packages built from the same checkout, which `tests/Trax.Docs.Snippets.Tests` references with
`ProjectReference`:

~~~markdown
```csharp compile
var every5Minutes = Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2));
```
~~~

The website ignores the extra word, so readers do not see it. Mark a fence when it is meant to be
copied as it stands; leave a fragment with `...` in it unmarked.

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

Examples on reference pages often lean on types the page only implies, such as the `ISyncTrain` a
scheduling example schedules. Declare those in `tests/Trax.Docs.Snippets.Tests/Context/`, at the
page's path with `.cs` for `.md` (`Context/sdk-reference/scheduler-api/schedule.cs`). That file
joins every compilation from the page, and can hold `global using` lines for the namespaces the
examples leave out. `getting-started.md` uses no context file: it is the page a reader copies
whole.

Every Trax `PackageReference` with a `Version` on a page names one exact version, the same on
every page, because every Trax package releases at one version; the lint lists each one that
differs. A production build of the site stamps the latest release version into each of them, so a
reader installs the release the site was published from.

