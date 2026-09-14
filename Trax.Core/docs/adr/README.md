# Decisions

Why a thing in `Trax.Core` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Core` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`core/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. That job consumes the guard as a
published action, so CI needs no other repo checked out. Running it locally does need
`../Trax.Docs` beside this repo, and the same flags the job passes, or the census is not
checked at all:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas platform,testing,packaging \
  --census-root tests/Trax.Core.Tests.Meta
```

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `platform` | [0001](./0001-chain-composition-errors-are-compile-time.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-chain-composition-errors-are-compile-time.md) | A mis-composed chain does not compile | platform |
