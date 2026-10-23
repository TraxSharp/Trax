---
title: trax-console
description: "trax-console, the operations console in a terminal: what it shows, how it signs in, its keys, and its JSON lines mode for scripts and agents."
type: reference
area: tooling
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.Console.Terminal/ConsoleCommandLine.cs
  - Trax.Api/src/Trax.Api.Console.Terminal/Connection
  - Trax.Api/src/Trax.Api.Console.Terminal/Lists
  - Trax.Api/src/Trax.Api.Console.Terminal/Screens
  - Trax.Api/src/Trax.Api.Console.Terminal/Headless
---

# trax-console

`trax-console` is the operations console in a terminal, for a host an operator reaches over SSH or anywhere without a
browser. It sends only the documents the browser console sends, through a client `trax client --operations --lang
csharp` writes from them ([The operations console's client](/docs/reference/cli#the-operations-consoles-client)), so a
host that serves the console registers everything it sends and the operations gate decides every request.

```bash
dotnet tool install --global Trax.Api.Console.Terminal
export TRAX_CONSOLE_API_KEY=$(pass show trax/prod-operator)
trax-console --host https://ops.example.com
```

## What the host needs

| The host | Why |
|---|---|
| `ExposeOperationQueries()`, and `ExposeOperationMutations()` for the actions | Everything the console reads is under `operations`. |
| An API key or JWT scheme the operations gate accepts | The terminal sends a credential; it cannot sign in through a browser. A host that signs operators in only by OIDC or a cookie (`HostAuthenticates()`) has nothing it can use. |
| `UseTraxApiDashboard(...)` or [`UseTraxOperatorConsoleDocuments()`](/docs/sdk-reference/api-dashboard/use-trax-operator-console-documents) | Either registers the console's documents on a host that requires persisted operations. The second serves no page. |
| The same Trax release as the tool | The documents are content addressed: a host of another release registers other ids. The console refuses to start against one and names the version to install; `--any-version` starts anyway, and the documents that differ are refused. |

A host that runs no persisted operations takes the documents' text instead: give `--send-text`.

## Options

| Option | What it does |
|---|---|
| `--host` | The host, such as `https://ops.example.com`. Or `TRAX_CONSOLE_HOST`. |
| `--graphql-path` | Where its Trax GraphQL endpoint is mapped (default `/trax/graphql`). The socket is the same path over `wss`. |
| `--api-key-file`, `--token-file` | A file holding the API key (sent as `X-Api-Key`) or the bearer token. Or `TRAX_CONSOLE_API_KEY`, `TRAX_CONSOLE_TOKEN`. A file wins over the environment, and only one credential may be given. The file must be a regular file of at most 64 KiB that no other user can read or change (`chmod 600`); any other is refused. There is no option that takes the credential itself: it would be in the shell's history and the process list. |
| `--send-text` | Send each document's text rather than its id. |
| `--any-version` | Talk to a host of another Trax release. It passes only a release that differs: a host that refuses the credential or cannot be reached is still refused. |
| `--vertical-graphs` | Draw machines top to bottom rather than left to right, in the console and in `graph`. |

A credential is sent over HTTPS only, or to this machine; `http://` to another host is refused.

## The console

A tab per list (runs, dead letters, the work queue, manifests) and one for machines, the live feed of run state
changes under them, and a status line. Runs, manifests and the feed leave out the scheduler's own trains, as the
browser console does; the filter `admin-trains=show` brings them back to a list. A list is read again a moment after the host says it changed
(`onDataChanged`), and when the socket comes back after a drop.

| Key | What it does |
|---|---|
| `n`, `p` | The next and previous page, by the cursor the host gave. |
| `f` | Filter the list; an empty field clears its filter. |
| space | Mark a row. An action runs on the marked rows, or on the current one when none is marked. |
| enter | The row's detail: everything the document selects. |
| `c` | Runs and work queue: cancel. |
| `r`, `a` | Dead letters: requeue (replaying recorded answers, or asking afresh), acknowledge with a note. |
| `t`, `e`, `d` | Manifests: trigger, enable, disable. |
| `b` | All matching: one of the list's actions on every row the filter matches, run by the host as a bulk operation. The count is the one the list shows, and the host refuses it when the filter now matches another. |
| `v` | A run's trace: its steps and decisions in time order, then its log, the steps followed live. It reads at most 500 of each. |
| `o` | Open the source of the train a row names, or of a step in a trace, in `$VISUAL` or `$EDITOR`, from the checkout `trax-console` runs in. The console comes back where it was when the editor exits. |
| F5, F1, Ctrl+Q | Read everything again, list the keys, quit. |

Everywhere, `j` and `k` move, `g` and `G` go to the top and the end, `Ctrl+d` and `Ctrl+u` move half a page, `h` and
`l` go to the tab before and after, `1` to `5` go to a tab by number, and `q` closes a trace or an instance, or quits.
The arrows work too.

The machines tab draws each machine as boxes and arrows: states in columns by how far they are from the initial one,
each transition a line labelled with its command (`on done` for the outcome of a train the state invokes, `after 30m`
for a timer), the current state of an instance in a double border. A transition three or more states take the same
way (`StartOver` from anywhere) is said once under the graph rather than drawn from each. `r` turns the drawing
between left to right and top to bottom (`--vertical-graphs` starts it top to bottom), and `H` and `L` scroll a graph
wider than the screen. It also lists the machine's instances (`s` keeps the stuck ones), and opens one with its place
on the graph, what it waits on, the runs it invoked and its history, read again when it changes. It is read only: the
operations surface sends a machine no command.

```
                                 ╭─────────╮                          ╭───────────╮
               ┌───after 30m────▶│ Cleared │                        ┌▶│ Cancelled │
    ┌──────────┴┐                ╰─────────╯                        │ ╰───────────╯
 ●─▶│ Cart      ├──PlaceOrder──┐                                    │
    │ ↻ AddItem │              │                                    │
    └───────────┘              │ ┌─────────┐                        │ ╭───────────╮
                               └▶│ Ordered ├──on cancelled, failed──┘ │ Delivered │
                                 └────────┬┘                          ╰───────────╯
                                          │                            ▲
                                          └──────────on done───────────┘
```

## JSON lines

With a command, `trax-console` prints JSON, one value per line, and says what went wrong on standard error. It exits
with:

| Code | When |
|---|---|
| 0 | The command did what it says. `watch` ends 0 on Ctrl+C, and 1 when the host ends the stream with an error. |
| 1 | The host refused, could not be reached, or runs another Trax release; an action or bulk operation that did not take. |
| 2 | The command line is wrong: no host, two credentials, an unknown list, filter or action, a missing `--note`, a `--take` out of range. |
| 130 | Interrupted while waiting for the host's version. |

A host that answers `CLIENT_OUTDATED` in the middle of a session, after a deploy, is refused like one of another
release at connect: the refusal names the code and the install line, here and in the console.

| Command | What it prints |
|---|---|
| `list <list> [--where name=value]... [--take n] [--after cursor]` | A page of runs, dead-letters, work-queue or manifests, a row per line as the host wrote it; the total and the next cursor on standard error. `--take` is 25 by default and at most 500; page on with `--after`. An unknown filter is refused with the list's filters. |
| `do <list> <action> <id>... [--note] [--ask-afresh] [--dry-run]` | What the host said of the action. |
| `bulk <list> <action> --where ... --expect n [--note] [--ask-afresh] [--dry-run]` | The bulk operation it started, then its progress until it ends. |
| `watch runs`, `watch changes`, `watch run <id>`, `watch machines [name]` | A stream, an event per line, until interrupted. |
| `trace <run>` | A run's steps, decisions and log, at most 500 of each. When one of them reaches 500 there may be more, and a line on standard error names it: `{"truncated":["step"],"limit":500}`. |
| `graph [machine]` | Each machine the host serves, or the one named, drawn as above (top to bottom with `--vertical-graphs`). Plain text, not JSON. |
| `check` | The host's release, and whether this console serves it. |

```bash
trax-console list dead-letters --where status=awaiting_intervention --take 100 | jq .id
trax-console bulk dead-letters requeue --where status=awaiting_intervention --expect 312 --dry-run
```

An agent run this way is an operator like any other: it holds its own key, the gate checks its roles, and the audit
names it.

## SDK Reference

> [UseTraxOperatorConsoleDocuments](/docs/sdk-reference/api-dashboard/use-trax-operator-console-documents) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [Trax.Client](/docs/sdk-reference/client-api/dotnet-client)
