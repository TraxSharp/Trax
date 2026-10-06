# Decisions

Why a thing in `Trax.Mediator` is the way it is, which alternatives were weighed, and what
each cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is
a deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Mediator` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several folders. Cite one of these as
`mediator/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/ci.yml`, at the repository root, runs the guard against
this directory on every pull request that touches this folder or a folder upstream of it. The
guard is built from `Trax.Docs/tools/Trax.Adr.Guard` in the same checkout. To run the same check
locally, from this folder, pass the flags the job passes (`.github/ci/packages.json`), or the
census is not checked at all:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas auth,platform,testing \
  --census-root tests/Trax.Mediator.Tests.Meta
```

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `auth` | [0001](./0001-authorization-is-fail-closed.md), [0005](./0005-a-train-run-by-name-is-the-train-that-runs.md) |
| `platform` | [0001](./0001-authorization-is-fail-closed.md), [0002](./0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md), [0003](./0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md), [0004](./0004-an-onqueue-hook-runs-under-a-time-limit.md), [0005](./0005-a-train-run-by-name-is-the-train-that-runs.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-authorization-is-fail-closed.md) | A gated train with no authorization service refuses to start | auth, platform |
| [0002](./0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md) | An enqueue resolves its train in a scope of its own | platform |
| [0003](./0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md) | A nested enqueue joins the enqueue it runs inside | platform |
| [0004](./0004-an-onqueue-hook-runs-under-a-time-limit.md) | An OnQueue hook runs under a time limit | platform |
| [0005](./0005-a-train-run-by-name-is-the-train-that-runs.md) | A train run by name is the train that runs | auth, platform |
