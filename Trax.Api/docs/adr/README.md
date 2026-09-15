# Decisions

Why a thing in `Trax.Api` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Api` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`api/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job needs nothing else cloned.
The local command does, because it builds the guard out of a workspace checkout:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas graphql,auth,platform,testing \
  --census-root tests/Trax.Api.Tests.Meta
```

Without `--census-root` the census is never added to the run, so nothing named
`census/classified` is printed and the local command is weaker than the job above.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `graphql` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md) |
| `platform` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-a-misconfigured-host-fails-at-startup.md) | A misconfigured host fails at startup, not at request time | graphql, platform |
| [0002](./0002-reading-the-service-collection-is-order-dependent.md) | Reading the service collection during registration is order-dependent | graphql, platform |
