---
layout: default
title: Contributing to the Docs
parent: Reference
nav_order: 14
---

# Contributing to the Docs

These pages are rendered by a Next.js site, not Jekyll. Four lints run in CI and fail the
build, so the conventions below are checked rather than requested.

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

Every `/docs/` link must resolve to a real file. A broken one is a 404 on the live site and
reads exactly like a working link in a diff, which is why it is machine-checked.

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
`index.md`, and the per-area overview pages such as `effect.md`); and thirteen named paths.
Those last are not a licence to skip the block. Ten are pages whose code is shell commands,
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
