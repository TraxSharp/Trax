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

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`mediator/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job needs no other repo present.
Running the same check locally does: it builds the guard from a workspace checkout of
Trax.Docs. Pass `--census-root` as well, or the census is never added to the run at all.

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
| `auth` | [0001](./0001-authorization-is-fail-closed.md) |
| `platform` | [0001](./0001-authorization-is-fail-closed.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-authorization-is-fail-closed.md) | A gated train with no authorization service refuses to start | auth, platform |
