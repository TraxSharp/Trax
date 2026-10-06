---
name: recording-decisions
description: Record an architecture decision as an ADR in the Trax repository. Use when a change makes a decision that is hard to reverse, surprising without context, and the result of a real trade-off, or when editing, superseding or reviewing an existing ADR.
---

# Recording decisions

Trax records architecture decisions as ADRs. A documentation page says what a rule *is*; an
ADR says whether it is a deliberate constraint or an accident, so a reader can tell which
rules are safe to change.

The format is [`ADR-FORMAT.md`](./ADR-FORMAT.md). Read it before writing one.

## When to offer an ADR

All three must be true:

1. **Hard to reverse.** The cost of changing your mind later is meaningful.
2. **Surprising without context.** A future reader will look at the code and wonder "why on
   earth did they do it this way?"
3. **The result of a real trade-off.** There were alternatives and you picked one
   for specific reasons.

If a decision is easy to reverse, skip it: you will just reverse it. If it is not
surprising, nobody will wonder why. If there was no real alternative, there is nothing to
record beyond "we did the obvious thing".

### What qualifies

- **Architectural shape.** How the repos layer, what depends on what.
- **Technology choices that carry lock-in.** The database, the migration mechanism, the
  GraphQL server. Not every library, just the ones that would take a quarter to swap.
- **Deliberate deviations from the obvious path.** Hand-written SQL instead of EF
  migrations, because X. Anything where a reasonable reader would assume the opposite.
  These stop the next engineer from "fixing" something that was deliberate.
- **Constraints not visible in the code.** A runtime version floor forced by an upstream
  package, a behaviour that exists because of a bug that shipped twice.
- **Rejected alternatives when the rejection is non-obvious.** Otherwise someone proposes
  it again in six months.

## Where it goes

**Does the decision bind more than one folder?** (The repository holds one folder per
package family: `Trax.Core`, `Trax.Effect`, ... `Trax.Docs`, `Trax.Website`.)

- **Yes:** `Trax.Docs/adr/`. Declare `repos:` listing every folder whose developers could
  break it without realising (the key keeps its old name; its values are folder slugs).
- **No:** `<Folder>/docs/adr/` in the folder it governs. **Omit** `repos:`, because the path
  already says it and a second place to say it is a second place to drift.

Numbering is per directory, so the same number exists in several folders. Cite a
folder-scoped ADR as `effect/0001`.

## The five steps, four of which the build enforces

| | Step | Enforced by |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | nobody, this is the human step |
| 2 | Tag it `repos` (central only) and `areas`, and add it to the index | `frontmatter/*`, `index/*` |
| 3 | Say where it stands in `## Status`, and record it in `## Changelog` | `lifecycle/*` |
| 4 | Give it `## Exemplars` naming guards, or `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | `exemplars/section` |
| 5 | Have each locally named guard cite the ADR back | `exemplars/guards-cite-back` |

A sixth step applies where the census is switched on with `--census-root`: every guard class
under that root must be named by an ADR or carry `Not ADR-enforcing: <reason>` in its own
docstring. Every folder the guard runs over switches it on for its `Tests.Meta` project
(`tests/Trax.Docs.Tests` in Trax.Docs).

Step 1 is the only one you have to remember, because no test can detect a decision you
chose not to record. Everything after it fails the build until it is done.

## Running the checks locally

From the repository root, against one folder:

```bash
dotnet run --project Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo Trax.Core --known-areas <the folder's vocabulary>
```

Each folder's vocabulary, ADR directory and census root are in `.github/ci/packages.json`,
under its `adr` entry. CI runs the same tool through the local `.github/actions/adr-guard`
action, once per folder that has a corpus: the eight code folders over their own `docs/adr/`,
and Trax.Docs over the central corpus. The guard is built from the same commit it checks, so
a guard change and the ADRs it newly fails land in one pull request. Trax.Website has no
corpus of its own, though `website` is a valid `repos` slug and a central ADR can bind it.

## Writing it

**Keep it short.** An ADR carries the decision, the alternatives and why they lost, and the
costs that are not obvious. Mechanism (how the thing works, what to type, which file to
edit) belongs in a documentation page or next to the code, and goes in `## Exemplars` as a
link. If you find yourself explaining *how*, you are writing the wrong document. If the
explanation has no home yet, write it there and link it rather than growing the ADR.

**No em-dashes.** The guard's hygiene check rejects them wherever the ADR lives. In Trax.Docs
`NoEmDashesTests` covers every `.md` in the folder as well, the central ADR corpus included; in
the other folders the hygiene check is the only thing that sees an ADR.

## When your change contradicts one

Say so. Do not silently override an ADR. Either the decision still holds and your change is
wrong, or the decision has changed and the ADR needs superseding, which is two edits: the
new ADR says `Supersedes` and links back, the old one sets `status: superseded-by-NNNN` and
links forward. The guard fails a half-done supersession, because a reader arriving from a
code comment lands on the old document.
