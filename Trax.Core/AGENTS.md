# Trax.Core

The foundation: trains, junctions, railway error propagation, the memory dictionary, the
Roslyn analyzer, and the `Trax.Core.Testing` guard engines. It depends on nothing in the
workspace, and the other seven code repos all depend on it, directly or through
`Trax.Effect`, so a change here reaches every one of them and every consumer. Trax.Docs and
Trax.Website hold no .NET reference to it.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| the analyzer, or a new diagnostic | [0001](./docs/adr/0001-chain-composition-errors-are-compile-time.md), the ids are a published contract and the severity is deliberate |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Eight name `core`: executable guards, exact version pinning, the
dependency direction, the three test conventions (FluentAssertions, no `[Ignore]`, no fixed
delays), the documentation lints, and the public API baseline. In a workspace checkout the
index is at `../Trax.Docs/adr/README.md`; that path does not resolve on GitHub, because it
crosses a repository boundary.

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were real alternatives), it takes five steps and
the build enforces four. The `adr-guard` job runs on every pull request.

| | Step | Enforced |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | no, this is the human step |
| 2 | Tag it `areas`, and add it to `docs/adr/README.md` | yes |
| 3 | Say where it stands in `## Status` and record it in `## Changelog` | yes |
| 4 | Give it `## Exemplars`: guards, `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | yes |
| 5 | Have each guard you named cite the ADR back, in its docstring and its failure message | yes |

Step 1 is the only one you have to remember, because no test can detect a decision you chose
not to record. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](./.claude/skills/recording-decisions/ADR-FORMAT.md).

## Guards

`tests/Trax.Core.Tests.Meta/` holds nine convention guards, and **all nine are shared** with
the other repos. Trax.Core owns no repo-specific guard, which is expected: the conventions it
would enforce are workspace-wide, and the engines behind several of them ship from here in
`Trax.Core.Testing` for consumers to subclass.

The census is on: every guard class under that folder is either credited to an ADR or
carries `Not ADR-enforcing:` with a reason, and the `adr-guard` job checks it. A new guard is
unclassified until you choose, and the build says so. Opting out is a normal answer; a reason
that reads as a deferral is not.

## Running the tests

```bash
dotnet test
```
