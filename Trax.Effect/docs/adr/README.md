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
| `auth` | [0004](./0004-trax-owns-the-authorization-vocabulary.md), [0008](./0008-per-user-data-is-filtered-by-its-owner.md) |
| `data-model` | [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md), [0003](./0003-a-model-and-its-persistent-mapping-are-a-pair.md), [0005](./0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md), [0006](./0006-a-closed-vocabulary-is-a-postgres-enum.md), [0007](./0007-cancelled-staged-entries-are-deleted-after-a-retention.md), [0008](./0008-per-user-data-is-filtered-by-its-owner.md), [0010](./0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md), [0013](./0013-a-request-id-replays-only-the-request-it-recorded.md) |
| `migrations` | [0001](./0001-schema-changes-are-hand-written-sql.md), [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md), [0006](./0006-a-closed-vocabulary-is-a-postgres-enum.md) |
| `platform` | [0004](./0004-trax-owns-the-authorization-vocabulary.md), [0005](./0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md), [0007](./0007-cancelled-staged-entries-are-deleted-after-a-retention.md), [0009](./0009-a-service-train-does-its-work-in-junctions.md), [0010](./0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md), [0011](./0011-a-service-train-instance-is-one-run.md), [0012](./0012-the-signalr-sink-queues-events-and-drops-when-clients-fall-behind.md), [0013](./0013-a-request-id-replays-only-the-request-it-recorded.md) |
| `providers` | [0001](./0001-schema-changes-are-hand-written-sql.md), [0006](./0006-a-closed-vocabulary-is-a-postgres-enum.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-schema-changes-are-hand-written-sql.md) | Schema changes are hand-written SQL journaled by DbUp | migrations, providers |
| [0002](./0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md) | Trax's tables are migrated; a consumer's domain tables are bootstrapped | migrations, data-model |
| [0003](./0003-a-model-and-its-persistent-mapping-are-a-pair.md) | A model and its persistent mapping are a pair | data-model |
| [0004](./0004-trax-owns-the-authorization-vocabulary.md) | Trax owns the authorization vocabulary | auth, platform |
| [0005](./0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md) | A train's outcome is recorded on a token the caller cannot cancel | platform, data-model |
| [0006](./0006-a-closed-vocabulary-is-a-postgres-enum.md) | A closed vocabulary Trax persists is a Postgres enum | data-model, migrations, providers |
| [0007](./0007-cancelled-staged-entries-are-deleted-after-a-retention.md) | Cancelled staged entries are deleted after a retention | data-model, platform |
| [0008](./0008-per-user-data-is-filtered-by-its-owner.md) | Per-user data is scoped by a row filter that reads the principal, and exposed as a bare [TraxAuthorize] | auth, data-model |
| [0009](./0009-a-service-train-does-its-work-in-junctions.md) | A service train does its work in Junctions(), and neither its Run nor its NewMonad can be overridden | platform |
| [0010](./0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md) | A sensitive field is marked, and masked where its copy is written | platform, data-model |
| [0011](./0011-a-service-train-instance-is-one-run.md) | A service train instance is one run at a time, and is never a singleton | platform |
| [0012](./0012-the-signalr-sink-queues-events-and-drops-when-clients-fall-behind.md) | The SignalR sink queues events, and drops them when clients fall behind | platform |
| [0013](./0013-a-request-id-replays-only-the-request-it-recorded.md) | A request id replays only the request it recorded | platform, data-model |
