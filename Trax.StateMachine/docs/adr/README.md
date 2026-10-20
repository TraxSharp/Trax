# Decisions

Why a thing in `Trax.StateMachine` is the way it is, which alternatives were weighed, and what
each cost. A documentation page tells you what the rule *is*; an ADR tells you whether it
is a deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.StateMachine` only**, in every language it holds. A decision binding more than
one Trax folder lives in the central corpus, at `Trax.Docs/adr/`, and declares which folders must
obey it; the machines' central ones are 0046 to 0048. These omit that key, because the path
already says it.

Numbering is per directory, so `0001` exists in several folders. Cite one of these as
`statemachine/0001`. 0001 to 0003 were `effect/0013`, `effect/0017` and `effect/0022` until the
machines left Trax.Effect on 2026-10-20.

## How they are checked

The `adr-guard` job in `.github/workflows/ci.yml`, at the repository root, runs the guard against
this directory on every pull request that touches this folder or a folder upstream of it. The
guard is built from `Trax.Docs/tools/Trax.Adr.Guard` in the same checkout. To run the same check
locally, from this folder, pass the flags the job passes (`.github/ci/packages.json`), or the
census is not checked at all:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --test-roots dotnet/tests \
  --known-areas platform,data-model,testing,auth \
  --census-root dotnet/tests/Trax.StateMachine.Tests.Meta
```

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `data-model` | [0001](./0001-a-request-id-replays-only-the-request-it-recorded.md), [0002](./0002-only-the-effect-runner-reaches-a-committed-state.md), [0003](./0003-a-machine-instance-history-is-written-with-its-snapshot.md) |
| `platform` | [0001](./0001-a-request-id-replays-only-the-request-it-recorded.md), [0002](./0002-only-the-effect-runner-reaches-a-committed-state.md), [0003](./0003-a-machine-instance-history-is-written-with-its-snapshot.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-a-request-id-replays-only-the-request-it-recorded.md) | A request id replays only the request it recorded | platform, data-model |
| [0002](./0002-only-the-effect-runner-reaches-a-committed-state.md) | Only the effect runner puts a draft into a committed state or an effect's target | platform, data-model |
| [0003](./0003-a-machine-instance-history-is-written-with-its-snapshot.md) | A machine instance's history is written by the store, with its snapshot, and goes with the instance | data-model, platform |
