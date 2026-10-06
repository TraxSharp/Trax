# Decisions

Why a thing in `Trax.Cli` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Cli` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`cli/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job needs no other repo checked
out. The local command below is not the job: it runs the guard from source, so it needs
Trax.Docs beside this repo in a workspace checkout.

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas tooling,platform,testing \
  --census-root tests/Trax.Cli.Tests.Meta
```

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `platform` | [0001](./0001-the-machine-toolchain-is-half-in-process.md) |
| `tooling` | [0001](./0001-the-machine-toolchain-is-half-in-process.md), [0002](./0002-generated-code-refuses-a-schema-name-it-cannot-emit.md), [0004](./0004-generate-force-replaces-only-on-success.md), [0005](./0005-a-machine-id-is-kebab-case.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-the-machine-toolchain-is-half-in-process.md) | The machine toolchain runs C# in process and TypeScript by shelling to node | tooling, platform |
| [0002](./0002-generated-code-refuses-a-schema-name-it-cannot-emit.md) | Generated code refuses a schema name it cannot emit, rather than rewriting it | tooling |
| [0004](./0004-generate-force-replaces-only-on-success.md) | `generate --force` replaces its output only on success, and never a repository | tooling |
| [0005](./0005-a-machine-id-is-kebab-case.md) | A machine id is kebab-case, and `trax machine` refuses any other | tooling |
