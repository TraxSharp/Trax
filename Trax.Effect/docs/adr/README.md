# Decisions

Why a thing in `Trax.Effect` is the way it is, which alternatives were weighed, and what
each cost. A documentation page tells you what the rule *is*; an ADR tells you whether it
is a deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Effect` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`effect/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. That job needs nothing else cloned:
it restores the published guard. To run the same check locally you need the Trax.Docs
checkout the workspace already has:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas migrations,providers,data-model,testing,platform \
  --census-root tests/Trax.Effect.Tests.Meta
```

`--census-root` is not optional in practice: without it the census is never added to the run
at all, so nothing named `census/classified` is printed and the local command is weaker than
the job it is standing in for.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `data-model` | [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md), [0003](./0003-a-model-and-its-persistent-mapping-are-a-pair.md) |
| `migrations` | [0001](./0001-schema-changes-are-hand-written-sql.md), [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md) |
| `providers` | [0001](./0001-schema-changes-are-hand-written-sql.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-schema-changes-are-hand-written-sql.md) | Schema changes are hand-written SQL journaled by DbUp | migrations, providers |
| [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md) | Trax's tables are migrated; a consumer's domain tables are bootstrapped | migrations, data-model |
| [0003](./0003-a-model-and-its-persistent-mapping-are-a-pair.md) | A model and its persistent mapping are a pair | data-model |
