---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# The console is organised by what an operator asks, and speaks one vocabulary

The console grew one page per table: eight peers under "Data", a run called an execution here and metadata there,
one action named "Trigger", "Run Now" and "Run Group" on three pages, and C# method names in its empty states. People
who did not already know every Trax feature could not find their way. From 2026-10-18 the console is organised by
the thing an operator works with, and every page takes its words from one table.

## Status

**Accepted.**

## The navigation

| Entry | What it holds |
|---|---|
| Overview | What needs attention, each item a link to the filtered list behind it |
| Runs | One list of runs, with views: All, Failed, Queued (the work queue), Dead letters |
| Trains | Every registered train; a train's page holds its chain, runs, manifests, the machines that invoke it, and Run and Queue |
| Machines | Every registered machine; a machine's page holds its diagram and its instances |
| Schedules | Manifests and manifest groups |
| System | Hosts, the scheduler's switches, retry and retention, effects, log levels, logs, live events, bulk operations |
| API admin | Persisted operations, shown only to a caller the host lets manage them |

Preferences, which this browser keeps for itself, sit at the foot of the sidebar. An old route redirects to its
new one, so a link in a log line keeps working.

## The vocabulary

- **Trax's own terms stay** (central [0013](../../../Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md)):
  a manifest is a manifest, a junction a junction. Each one links to its glossary entry where it first appears on a page.
- **A run is a run.** Never "metadata", "execution" or "job", which are storage or other products' names.
- **One verb per action, the same on every page.** Run starts a train now and skips the queue. Queue adds work to
  the queue. Requeue repeats a run, or a dead letter's manifest. Resume continues a run after its checkpoint. Cancel,
  Acknowledge, Enable and Disable mean what they say. Pause and Resume apply to the scheduler. Ask afresh is an
  option of Requeue and Queue, offered only for a train that asks a decider.
- **An enum value has a display name**: "Awaiting intervention", not `AwaitingIntervention`.
- **Page text never names a C# API.** An empty state says what fills the page and links to the docs page that shows how.

The table lives in one ClientApp module that every page imports.

## Considered options

**Rename Trax's concepts for the console** (schedule for manifest, step for junction). Rejected: the console would
then disagree with the docs, the code and the GraphQL schema, and an operator moving between them would meet two
names for one thing. Explaining the term where it appears costs less than translating it.

**Keep one page per table, and add help text.** Most pages already carried paragraphs of explanation, and the
confusion stayed, because the problem was where things were, not what they were called.

## Exemplars

**Unenforced:** whether a page uses the right verb, or puts a thing under the right entry, is a reading of the page.
A string scan can refuse known wrong words, but it cannot show that a new button chose the right one.

Not covered: the navigation table above is the decision; the routes in `ClientApp/src/AppRoutes.tsx` are what
implements it, and nothing compares the two.

## Changelog

- **2026-10-18**: Recorded.
