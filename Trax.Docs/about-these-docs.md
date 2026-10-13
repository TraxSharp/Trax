---
title: How these docs are organised
description: "The four kinds of page in the Trax docs, what each is for, how the sidebar groups them, and which code samples are compiled in CI."
type: explanation
area: general
nav_order: 1
---

# How these docs are organised

Every page in these docs is one of four kinds, and says which above its title. A page does one job,
so when you want something else (the reason behind a step, the full option list behind an example)
it links to the page that has it rather than repeating it.

## The four kinds of page

| Kind | What it is for | In Trax |
|---|---|---|
| Tutorial | Learning by doing. One path from an empty folder to something that runs, with a visible result at each step. | [Getting Started](/docs/getting-started) builds a server with a train, a schedule, the dashboard and a GraphQL API. |
| How-to guide | Getting a specific job done when you already know what you want. Steps, no teaching. | Setting up the scheduler, resolving a dead letter, securing a GraphQL host, testing a train. |
| Reference | Looking something up. Describes what exists and what it does, in a consistent shape, without advice. | Every public type and method under [SDK Reference](/docs/sdk-reference), the CLI, the packages, the glossary, the schema migrations. |
| Explanation | Understanding. Why a part of Trax works the way it does, what it trades off, how its pieces fit. | How a train runs its chain, what an effect is, how the scheduler decides when a manifest runs. |

If you are new, start with the tutorial. If you have a task, look under How-to guides. If you know
the name of the thing, use the reference. If you want to know why, read the explanation.

## The sidebar

The sidebar is grouped by kind first: Tutorials, How-to guides, Explanation, Reference and SDK
Reference, then Samples and Contributing. Inside each kind, pages are grouped by area, in this
order: General, Trains, Effects, Data, Mediator, Scheduling, GraphQL API, Security, State machines,
Dashboard, Testing, Tooling, Upgrading.

At the foot of a page, "More on" lists the other pages of the same area, by kind, so you can move
from a how-to to the explanation behind it or the reference it uses.

Samples are tours of the runnable applications in
[Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples): what each one proves, how
to run it, and where to look in its code. Contributing holds what you need only to work on Trax
itself.

## SDK reference pages

Each page under [SDK Reference](/docs/sdk-reference) documents one type or method. Under its title
it names the NuGet package to install, the namespace to import, and links to the source files on
GitHub at the release the site was published from. The sections come in a fixed order: signature,
parameters or members, return value, behaviour, example, see also.

## Code samples

A code sample marked as compiled is built in CI against the Trax source the site was published
from, so it compiles against that release as written. Other samples are excerpts of a real file in
the repository, checked against it, or deliberate fragments such as signatures, which are not
compiled.

The site is published from releases, not from the main branch, so the pages describe the latest
released version. Every `Trax.*` package reference a page shows names that version.
