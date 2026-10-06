# Decisions

Why a thing in `Trax.Samples` is the way it is, which alternatives were weighed, and what
each cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is
a deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Samples` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`samples/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. That job needs nothing else cloned.
The local equivalent does, because it builds the guard out of the workspace checkout:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas testing,ci,samples \
  --census-root tests/Trax.Samples.Tests.Meta
```

Without `--census-root` the census is never added to the run, so nothing named
`census/classified` is printed and the local command is weaker than the job above.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `ci` | [0001](./0001-a-sample-e2e-database-must-be-one-ci-provisions.md) |
| `samples` | [0002](./0002-the-samples-adopt-the-guards-as-a-consumer-would.md), [0003](./0003-templates-serve-the-dashboard-only-in-development.md), [0004](./0004-the-template-package-carries-its-package-versions.md), [0005](./0005-sample-infrastructure-listens-on-loopback-only.md), [0006](./0006-one-sample-per-major-feature-proven-end-to-end.md) |
| `testing` | [0001](./0001-a-sample-e2e-database-must-be-one-ci-provisions.md), [0002](./0002-the-samples-adopt-the-guards-as-a-consumer-would.md), [0006](./0006-one-sample-per-major-feature-proven-end-to-end.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-a-sample-e2e-database-must-be-one-ci-provisions.md) | A sample's E2E database must be one CI actually provisions | testing, ci |
| [0002](./0002-the-samples-adopt-the-guards-as-a-consumer-would.md) | The samples adopt the shipped guards the way a consumer would | testing, samples |
| [0003](./0003-templates-serve-the-dashboard-only-in-development.md) | The templates serve the dashboard and the demo key only in Development | samples |
| [0004](./0004-the-template-package-carries-its-package-versions.md) | The template package carries its package versions, generated at pack from the central pins | samples |
| [0005](./0005-sample-infrastructure-listens-on-loopback-only.md) | Sample infrastructure listens on loopback only | samples |
| [0006](./0006-one-sample-per-major-feature-proven-end-to-end.md) | One sample per major feature, each proven end to end | samples, testing |
