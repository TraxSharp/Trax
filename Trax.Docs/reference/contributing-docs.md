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

Jekyll template syntax is rejected: the link and include tags, the baseurl variable, and
kramdown inline attribute lists. All three are leftovers from the site's Jekyll era and
render as literal text now. The lint names them precisely; this page does not spell them out,
because writing one would trip the lint it describes.

Every `/docs/` link must resolve to a real file. A broken one is a 404 on the live site and
reads exactly like a working link in a diff, which is why it is machine-checked.

## SDK reference blocks

Every page outside `sdk-reference/` that contains a fenced code block ends with a
consolidated block listing the SDK methods its examples use:

```markdown
## SDK Reference

> [AddTrax](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler)
```

One block per page, at the bottom, `|` as the separator. Only link methods that have a
dedicated SDK page. A page whose code is shell commands or SQL rather than SDK calls is
exempted by name in the lint, with a reason.

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
