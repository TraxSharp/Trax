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

Numbering is per directory, so `0001` exists in several folders. Cite one of these as
`core/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/ci.yml`, at the repository root, runs the guard against
this directory on every pull request that touches this folder or a folder upstream of it. The
guard is built from `Trax.Docs/tools/Trax.Adr.Guard` in the same checkout. To run the same check
locally, from this folder, pass the flags the job passes (`.github/ci/packages.json`), or the
census is not checked at all:

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
| `platform` | [0001](./0001-chain-composition-errors-are-compile-time.md), [0002](./0002-a-shipped-seam-stays-public-and-hidden.md), [0003](./0003-plain-train-keeps-an-overridable-run.md), [0004](./0004-a-recorded-answer-replays-only-into-the-same-state.md) |
| `packaging` | [0002](./0002-a-shipped-seam-stays-public-and-hidden.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-chain-composition-errors-are-compile-time.md) | A mis-composed chain does not compile | platform |
| [0002](./0002-a-shipped-seam-stays-public-and-hidden.md) | A shipped seam stays public, and is hidden rather than narrowed | platform, packaging |
| [0003](./0003-plain-train-keeps-an-overridable-run.md) | Plain Train stays a consumer base, and its Run stays overridable | platform |
| [0004](./0004-a-recorded-answer-replays-only-into-the-same-state.md) | A recorded answer replays only into the same state | platform |
