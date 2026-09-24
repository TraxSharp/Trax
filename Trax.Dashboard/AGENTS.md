# Trax.Dashboard

A Blazor Server monitoring UI that mounts into an existing application rather than running
as its own. Trains, runs, manifests and the dead-letter queue, rendered with Radzen. It sits
above `Trax.Api` and below `Trax.Samples`.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| `AddTraxDashboard` or `UseTraxDashboard` | [0001](./docs/adr/0001-the-dashboard-mounts-into-the-host-app.md), these run inside somebody else's application and change its middleware |
| the queue dialog, the Re-queue button, or anything else that enqueues | central `docs/0017`: enqueue through `IOperationsService` inside the `"dashboard"` trusted scope, never by building a work queue row; per-train `[TraxAuthorize]` does not apply because the dashboard is gated as a whole by its host |
| the work queue pages (Staged, Subject, Waiting On) or the Failure Class field | central `docs/0018`, `docs/0019` and `docs/0020` |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Nineteen name `dashboard`: the workspace-wide conventions, `0016`
to `0020` (the enqueue ones routed above), and the canonical train name being the interface
FullName, which this repo compares against when it looks a train up. In a
workspace checkout the index is at `../Trax.Docs/adr/README.md`; that path does not resolve
on GitHub, because it crosses a repository boundary.

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were real alternatives), it takes five steps and
the build enforces four. The `adr-guard` job runs on every pull request.

| | Step | Enforced |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | no, this is the human step |
| 2 | Tag it `areas`, and add it to `docs/adr/README.md` | yes |
| 3 | Say where it stands in `## Status` and record it in `## Changelog` | yes |
| 4 | Give it `## Exemplars`: guards, `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | yes |
| 5 | Have each guard you named cite the ADR back, in its docstring and its failure message | yes |

Step 1 is the only one you have to remember, because no test can detect a decision you chose
not to record. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](./.claude/skills/recording-decisions/ADR-FORMAT.md).

## Guards

`tests/Trax.Dashboard.Tests.Meta/` holds thirteen convention guards, and **all thirteen are
shared** with the other repos. This repo owns no convention guard of its own;
`WorkQueueCreationSitesTests` runs here with an empty allow-list, so no dashboard code may
build a work queue row (`docs/0017`). The gaps that matter are
named in the `## Exemplars` section of
[0001](./docs/adr/0001-the-dashboard-mounts-into-the-host-app.md), in descending order of
reach. The first is not a test gap at all: the dashboard applies no authorization of its own,
so an unguarded host serves `/trax` to anyone who can reach the port. Then `RoutePrefix`
moving the sidebar but not the pages; then nothing asserting what `UseTraxDashboard()` does to
the host's middleware pipeline; then, inside the registration half, nothing exercising the
`WebApplicationBuilder` overload of `AddTraxDashboard()`, the one its own documentation calls
recommended and the one that mutates the host outside DI.

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

```bash
dotnet test
```
